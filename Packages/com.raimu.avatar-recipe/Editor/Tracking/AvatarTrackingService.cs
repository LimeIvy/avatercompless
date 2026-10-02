using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AvatarRecipe.Editor.Core.Diff;
using AvatarRecipe.Editor.Core.Models;
using AvatarRecipe.Editor.Core.Snapshot;
using AvatarRecipe.Editor.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AvatarRecipe.Editor.Tracking
{
    [Serializable]
    internal sealed class AvatarBaselineCache
    {
        public int schemaVersion = 2;
        public string projectId;
        public string globalObjectId;
        public string scenePath;
        public string fallbackHierarchyPath;
        public AvatarSnapshot snapshot;
        public BaseAvatarReference baseAvatar;
    }

    [InitializeOnLoad]
    internal static class AvatarTrackingService
    {
        private const string RecipeId = "avatar";
        private const string RecipeName = "Avatar";
        private static GameObject _root;
        private static AvatarSnapshot _baseline;
        private static string _error;
        private static bool _dirty;
        private static BaseAvatarReference _baseAvatarOverride;

        public static event Action Changed;
        public static GameObject Root => _root;
        public static bool IsTracking => _root != null && _baseline != null;
        public static bool IsDirty => _dirty;
        public static string Error => _error;
        public static bool HasKnownBaseAvatar => IsTracking && _baseAvatarOverride != null &&
            !string.IsNullOrEmpty(_baseAvatarOverride.assetPath);

        static AvatarTrackingService()
        {
            Undo.postprocessModifications += OnPostprocessModifications;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorSceneManager.sceneDirtied += OnSceneDirtied;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += RestoreTracking;
        }

        public static void StartTracking(GameObject root)
        {
            StartTrackingInternal(root, null, null);
        }

        public static void StartTrackingFromScan(GameObject root, AvatarSnapshot baseline, BaseAvatarReference baseAvatar)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (baseAvatar == null) throw new ArgumentNullException(nameof(baseAvatar));
            StartTrackingInternal(root, baseline, baseAvatar);
        }

        private static void StartTrackingInternal(GameObject root, AvatarSnapshot baseline, BaseAvatarReference baseAvatar)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var scene = root.scene;
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("Avatar Root must belong to a saved, loaded scene. Save the scene first.");
            }

            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(root).ToString();
            if (string.IsNullOrEmpty(globalId) || globalId.EndsWith("-0-0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unity could not create a persistent identity for this Avatar Root. Save the scene and try again.");
            }

            var reference = new TrackedAvatarReference
            {
                globalObjectId = globalId,
                scenePath = scene.path,
                fallbackHierarchyPath = GetHierarchyPath(root.transform)
            };
            var snapshot = baseline ?? AvatarSnapshotBuilder.Build(root);
            var effectiveBaseAvatar = baseAvatar ?? GetBaseAvatarReference(root);
            WriteBaseline(reference, snapshot, effectiveBaseAvatar);
            AvatarRecipeProjectSettings.SetTrackedAvatar(reference);
            _root = root;
            _baseline = snapshot;
            _baseAvatarOverride = effectiveBaseAvatar;
            _dirty = false;
            _error = string.Empty;
            NotifyChanged();
        }

        public static void RebuildBaseline()
        {
            var reference = AvatarRecipeProjectSettings.TrackedAvatar;
            var root = ResolveTrackedObject(reference, out var resolutionError);
            if (root == null)
            {
                throw new InvalidOperationException(resolutionError);
            }

            var snapshot = AvatarSnapshotBuilder.Build(root);
            WriteBaseline(reference, snapshot, _baseAvatarOverride);
            _root = root;
            _baseline = snapshot;
            _dirty = false;
            _error = string.Empty;
            NotifyChanged();
        }

        public static void StopTracking()
        {
            AvatarRecipeProjectSettings.SetTrackedAvatar(new TrackedAvatarReference());
            _root = null;
            _baseline = null;
            _baseAvatarOverride = null;
            _dirty = false;
            _error = string.Empty;
            NotifyChanged();
        }

        private static void RestoreTracking()
        {
            if (!string.IsNullOrEmpty(AvatarRecipeProjectSettings.LoadError))
            {
                _root = null;
                _baseline = null;
                _error = AvatarRecipeProjectSettings.LoadError;
                NotifyChanged();
                return;
            }

            var reference = AvatarRecipeProjectSettings.TrackedAvatar;
            if (string.IsNullOrEmpty(reference.globalObjectId) && string.IsNullOrEmpty(reference.fallbackHierarchyPath))
            {
                return;
            }

            _root = ResolveTrackedObject(reference, out _error);
            if (_root == null)
            {
                NotifyChanged();
                return;
            }

            var cachePath = GetBaselinePath();
            if (!File.Exists(cachePath))
            {
                _error = "Local baseline is missing. Rebuild it explicitly after confirming the current avatar is the intended baseline.";
                NotifyChanged();
                return;
            }

            try
            {
                var cacheJson = File.ReadAllText(cachePath, Encoding.UTF8);
                var cache = JsonUtility.FromJson<AvatarBaselineCache>(cacheJson);
                if (cache == null || (cache.schemaVersion != 1 && cache.schemaVersion != 2) ||
                    cache.projectId != AvatarRecipeProjectSettings.ProjectId ||
                    cache.snapshot == null || cache.globalObjectId != reference.globalObjectId ||
                    cache.scenePath != reference.scenePath || cache.fallbackHierarchyPath != reference.fallbackHierarchyPath)
                {
                    _error = "Local baseline is invalid or belongs to a different tracked Avatar. Rebuild it explicitly to continue.";
                    _baseline = null;
                    NotifyChanged();
                    return;
                }

                if (cacheJson.IndexOf("\"addedPrefabs\"", StringComparison.Ordinal) < 0)
                {
                    UpgradeLegacyPrefabBaseline(cache);
                }

                _baseline = cache.snapshot;
                _baseAvatarOverride = cache.baseAvatar ?? GetBaseAvatarReference(_root);
                _error = string.Empty;
                _dirty = false;
                NotifyChanged();
            }
            catch (Exception exception)
            {
                _baseline = null;
                _error = "Could not read local baseline: " + exception.Message;
                NotifyChanged();
            }
        }

        private static void UpgradeLegacyPrefabBaseline(AvatarBaselineCache cache)
        {
            var current = AvatarSnapshotBuilder.Build(_root);
            var baselinePaths = new HashSet<string>(cache.snapshot.transforms.ConvertAll(item => item.path), StringComparer.Ordinal);
            cache.snapshot.addedPrefabs = current.addedPrefabs
                .FindAll(prefab => baselinePaths.Contains(prefab.path));
            cache.schemaVersion = 2;
            var path = GetBaselinePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(cache, true) + "\n", new UTF8Encoding(false));
        }

        private static void WriteBaseline(TrackedAvatarReference reference, AvatarSnapshot snapshot,
            BaseAvatarReference baseAvatar)
        {
            var cache = new AvatarBaselineCache
            {
                projectId = AvatarRecipeProjectSettings.ProjectId,
                globalObjectId = reference.globalObjectId,
                scenePath = reference.scenePath,
                fallbackHierarchyPath = reference.fallbackHierarchyPath,
                snapshot = snapshot,
                baseAvatar = baseAvatar
            };
            var path = GetBaselinePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(cache, true) + "\n", new UTF8Encoding(false));
        }

        private static string GetBaselinePath()
        {
            return Path.Combine(AvatarRecipeProjectSettings.ProjectDirectory, "Library", "AvatarRecipe",
                AvatarRecipeProjectSettings.ProjectId, "baseline.json");
        }

        private static GameObject ResolveTrackedObject(TrackedAvatarReference reference, out string error)
        {
            error = "";
            if (reference == null)
            {
                error = "No tracked Avatar Root is configured.";
                return null;
            }

            if (!string.IsNullOrEmpty(reference.globalObjectId))
            {
                try
                {
                    if (GlobalObjectId.TryParse(reference.globalObjectId, out var globalId))
                    {
                        var id = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
                        if (id is GameObject globalObject && globalObject.scene.path == reference.scenePath)
                        {
                            return globalObject;
                        }
                        if (id is Transform globalTransform && globalTransform.gameObject.scene.path == reference.scenePath)
                        {
                            return globalTransform.gameObject;
                        }
                    }
                }
                catch (Exception)
                {
                    // Use the exact scene and hierarchy path fallback below.
                }
            }

            var scene = SceneManager.GetSceneByPath(reference.scenePath ?? string.Empty);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "Tracked Avatar scene is not loaded. Open the scene to resume tracking.";
                return null;
            }

            var matches = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                FindByPath(root.transform, "", reference.fallbackHierarchyPath ?? string.Empty, matches);
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            error = matches.Count == 0
                ? "Tracked Avatar Root could not be resolved in its saved scene. Reselect the root to resume tracking."
                : "Tracked Avatar hierarchy path is ambiguous. Reselect the root to resume tracking.";
            return null;
        }

        private static void FindByPath(Transform current, string parentPath, string wantedPath, List<GameObject> matches)
        {
            var segment = Uri.EscapeDataString(current.name);
            var path = string.IsNullOrEmpty(parentPath) ? segment : parentPath + "/" + segment;
            if (string.Equals(path, wantedPath, StringComparison.Ordinal))
            {
                matches.Add(current.gameObject);
            }

            for (var index = 0; index < current.childCount; index++)
            {
                FindByPath(current.GetChild(index), path, wantedPath, matches);
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var segments = new List<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                segments.Add(Uri.EscapeDataString(current.name));
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (IsTracking && modifications != null)
            {
                foreach (var modification in modifications)
                {
                    var target = modification.currentValue != null ? modification.currentValue.target : null;
                    if (target is Component component && IsUnderRoot(component.transform) ||
                        target is GameObject gameObject && IsUnderRoot(gameObject.transform))
                    {
                        MarkDirty();
                        break;
                    }
                }
            }
            return modifications;
        }

        private static void OnHierarchyChanged()
        {
            if (IsTracking && _root != null && _root.scene.IsValid())
            {
                MarkDirty();
            }
        }

        private static void OnSceneDirtied(Scene scene)
        {
            if (IsTracking && _root != null && scene == _root.scene)
            {
                MarkDirty();
            }
        }

        private static void OnSceneSaved(Scene scene)
        {
            if (_root == null || _baseline == null || scene.path != _root.scene.path)
            {
                return;
            }

            try
            {
                var current = AvatarSnapshotBuilder.Build(_root);
                var state = AvatarDiffEngine.Diff(_baseline, current);
                var baseAvatar = _baseAvatarOverride ?? GetBaseAvatarReference(_root);
                WriteRecipeFiles(state, baseAvatar);
                _dirty = false;
                _error = string.Empty;
            }
            catch (Exception exception)
            {
                _error = "Recipe was not written: " + exception.Message;
            }
            NotifyChanged();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!string.IsNullOrEmpty(AvatarRecipeProjectSettings.LoadError))
            {
                return;
            }
            var reference = AvatarRecipeProjectSettings.TrackedAvatar;
            if (!string.IsNullOrEmpty(reference.scenePath) && reference.scenePath == scene.path)
            {
                RestoreTracking();
            }
        }

        private static bool IsUnderRoot(Transform target)
        {
            return target != null && _root != null && (_root.transform == target || target.IsChildOf(_root.transform));
        }

        private static void MarkDirty()
        {
            if (_dirty)
            {
                return;
            }
            _dirty = true;
            NotifyChanged();
        }

        private static void NotifyChanged()
        {
            Changed?.Invoke();
        }

        private static BaseAvatarReference GetBaseAvatarReference(GameObject root)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (source == null)
            {
                return new BaseAvatarReference { name = root.name, prefabGuid = "", assetPath = "" };
            }

            var path = AssetDatabase.GetAssetPath(source);
            return new BaseAvatarReference
            {
                name = source.name,
                prefabGuid = AssetDatabase.AssetPathToGUID(path),
                assetPath = path
            };
        }

        private static void WriteRecipeFiles(RecipeState state, BaseAvatarReference baseAvatar)
        {
            var projectFolder = AvatarRecipeProjectSettings.GetRecipeProjectDirectory();
            var recipeFolder = Path.Combine(projectFolder, "recipes", "Avatar");
            if (string.IsNullOrWhiteSpace(AvatarRecipeProjectSettings.RecipeRoot) ||
                !Directory.Exists(AvatarRecipeProjectSettings.RecipeRoot))
            {
                throw new DirectoryNotFoundException("Recipe Root is unavailable: " + AvatarRecipeProjectSettings.RecipeRoot);
            }

            Directory.CreateDirectory(recipeFolder);
            var statePath = Path.Combine(recipeFolder, "state.json");
            WriteIfChanged(statePath, RecipeFileSerializer.SerializeState(state, baseAvatar));
            WriteIfChanged(Path.Combine(recipeFolder, "recipe.md"), RecipeFileSerializer.SerializeMarkdown(state, baseAvatar));
            var historyPath = Path.Combine(projectFolder, "history.json");
            WriteIfChanged(historyPath, RecipeFileSerializer.AppendHistory(File.Exists(historyPath)
                ? File.ReadAllText(historyPath, Encoding.UTF8) : string.Empty));
            EnsureProjectIndex(projectFolder);
        }

        private static void EnsureProjectIndex(string projectFolder)
        {
            var path = Path.Combine(projectFolder, "project.json");
            var projectName = AvatarRecipeProjectSettings.ProjectName;
            var recipes = new List<ProjectRecipeEntry>();
            if (File.Exists(path))
            {
                var existing = JsonUtility.FromJson<ProjectIndexFile>(File.ReadAllText(path, Encoding.UTF8));
                if (existing == null || existing.projectId != AvatarRecipeProjectSettings.ProjectId)
                {
                    throw new IOException("Existing project.json belongs to another project or is invalid: " + path);
                }
                if (existing.recipes != null)
                {
                    recipes = new List<ProjectRecipeEntry>(existing.recipes);
                }
            }

            if (!recipes.Any(recipe => recipe.id == RecipeId))
            {
                recipes.Add(new ProjectRecipeEntry { id = RecipeId, name = RecipeName, path = "recipes/Avatar" });
            }
            recipes.Sort((left, right) => StringComparer.Ordinal.Compare(left.id, right.id));
            var index = new ProjectIndexFile
            {
                schemaVersion = 1,
                projectId = AvatarRecipeProjectSettings.ProjectId,
                projectName = projectName,
                recipes = recipes.ToArray()
            };
            WriteIfChanged(path, JsonUtility.ToJson(index, true) + "\n");
        }

        private static void WriteIfChanged(string path, string contents)
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path, Encoding.UTF8), contents, StringComparison.Ordinal))
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents, new UTF8Encoding(false));
        }
    }

    [Serializable]
    internal sealed class BaseAvatarReference
    {
        public string name;
        public string prefabGuid;
        public string assetPath;
    }

    [Serializable]
    internal sealed class ProjectIndexFile
    {
        public int schemaVersion;
        public string projectId;
        public string projectName;
        public ProjectRecipeEntry[] recipes;
    }

    [Serializable]
    internal sealed class ProjectRecipeEntry
    {
        public string id;
        public string name;
        public string path;
    }

    internal static class RecipeFileSerializer
    {
        public static string SerializeState(RecipeState state, BaseAvatarReference avatar)
        {
            var builder = new StringBuilder();
            builder.Append("{\n  \"schemaVersion\": ")
                .Append(RecipeState.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\n  \"recipeId\": \"avatar\",\n  \"baseAvatar\": {\n")
                .Append("    \"name\": ").Append(Json(avatar.name)).Append(",\n")
                .Append("    \"prefabGuid\": ").Append(Json(avatar.prefabGuid)).Append(",\n")
                .Append("    \"assetPath\": ").Append(Json(avatar.assetPath)).Append("\n  },\n  \"prefabs\": [");

            var prefabs = (state.addedPrefabs ?? new List<AddedPrefabEntry>())
                .OrderBy(prefab => prefab.id, StringComparer.Ordinal)
                .ToList();
            for (var index = 0; index < prefabs.Count; index++)
            {
                var prefab = prefabs[index];
                builder.Append(index == 0 ? "\n" : ",\n")
                    .Append("    {\"id\": ").Append(Json(prefab.id))
                    .Append(", \"source\": {\"name\": ").Append(Json(prefab.name))
                    .Append(", \"guid\": ").Append(Json(prefab.guid))
                    .Append(", \"assetPath\": ").Append(Json(prefab.assetPath)).Append("}, \"placement\": {")
                    .Append("\"parentScope\": ").Append(Json(prefab.parentScope))
                    .Append(", \"parentPath\": ").Append(Json(prefab.parentPath))
                    .Append(", \"siblingIndex\": ").Append(prefab.siblingIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"localPosition\": ").Append(Vector(prefab.localPosition))
                    .Append(", \"localRotation\": ").Append(Quaternion(prefab.localRotation))
                    .Append(", \"localScale\": ").Append(Vector(prefab.localScale)).Append("}}\n");
            }
            builder.Append(prefabs.Count == 0 ? "\n  ],\n  \"warnings\": [" : "  ],\n  \"warnings\": [");
            var warnings = (state.manualReview ?? new List<string>()).Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal).ToList();
            for (var index = 0; index < warnings.Count; index++)
            {
                builder.Append(index == 0 ? "\n" : ",\n").Append("    ").Append(Json(warnings[index]));
            }
            builder.Append(warnings.Count == 0 ? "\n  ],\n  \"changes\": [" : "\n  ],\n  \"changes\": [");

            var changes = new List<string>();
            foreach (var change in state.transformChanges)
            {
                var vector = change.property == "localRotation"
                    ? "\"baseline\": " + Quaternion(change.baselineQuaternion) + ", \"value\": " + Quaternion(change.valueQuaternion)
                    : "\"baseline\": " + Vector(change.baselineVector3) + ", \"value\": " + Vector(change.valueVector3);
                changes.Add("    {\"kind\": \"transform\", \"target\": " + Target(change.target) +
                            ", \"property\": " + Json(change.property) + ", " + vector + "}");
            }
            foreach (var change in state.blendShapeChanges)
            {
                changes.Add("    {\"kind\": \"blendShape\", \"target\": " + Target(change.target) +
                            ", \"name\": " + Json(change.name) + ", \"baseline\": " + Number(change.baseline) +
                            ", \"value\": " + Number(change.value) + "}");
            }
            foreach (var change in state.activeStateChanges)
            {
                changes.Add("    {\"kind\": \"activeState\", \"target\": " + Target(change.target) +
                            ", \"baseline\": " + change.baseline.ToString().ToLowerInvariant() +
                            ", \"value\": " + change.value.ToString().ToLowerInvariant() + "}");
            }
            changes.Sort(StringComparer.Ordinal);
            builder.Append(changes.Count == 0 ? "\n" : "\n" + string.Join(",\n", changes) + "\n")
                .Append("  ]\n}\n");
            return builder.ToString();
        }

        public static string SerializeMarkdown(RecipeState state, BaseAvatarReference avatar)
        {
            var builder = new StringBuilder("# Avatar\n\n## Base Avatar\n\n");
            builder.Append("- Prefab: `").Append(Markdown(avatar.assetPath)).Append("`\n");
            if (string.IsNullOrEmpty(avatar.assetPath))
            {
                builder.Append("- Source Prefab could not be identified automatically.\n");
            }
            builder.Append("\n## Required Prefabs\n\n");
            foreach (var prefab in (state.addedPrefabs ?? new List<AddedPrefabEntry>()).OrderBy(entry => entry.id, StringComparer.Ordinal))
            {
                builder.Append("### ").Append(Markdown(prefab.name)).Append("\n\n")
                    .Append("- Prefab: `").Append(Markdown(prefab.assetPath)).Append("`\n")
                    .Append("- Parent: `").Append(string.IsNullOrEmpty(prefab.parentPath) ? "Avatar" : Markdown(prefab.parentPath)).Append("`\n")
                    .Append("- Sibling Index: `").Append(prefab.siblingIndex.ToString(CultureInfo.InvariantCulture)).Append("`\n")
                    .Append("- Local Position: `").Append(Vector(prefab.localPosition)).Append("`\n")
                    .Append("- Local Rotation: `").Append(Quaternion(prefab.localRotation)).Append("`\n")
                    .Append("- Local Scale: `").Append(Vector(prefab.localScale)).Append("`\n\n");
            }
            builder.Append("\n## Transform Changes\n\n");
            foreach (var change in state.transformChanges
                         .OrderBy(item => item.target.path, StringComparer.Ordinal)
                         .ThenBy(item => item.property, StringComparer.Ordinal))
            {
                var before = change.property == "localRotation" ? Quaternion(change.baselineQuaternion) : Vector(change.baselineVector3);
                var after = change.property == "localRotation" ? Quaternion(change.valueQuaternion) : Vector(change.valueVector3);
                builder.Append("- `").Append(Markdown(change.target.path)).Append("` `")
                    .Append(change.property).Append("`: `").Append(before).Append("` → `").Append(after).Append("`\n");
            }
            builder.Append("\n## BlendShape Changes\n\n");
            foreach (var change in state.blendShapeChanges
                         .OrderBy(item => item.target.path, StringComparer.Ordinal)
                         .ThenBy(item => item.target.componentId, StringComparer.Ordinal)
                         .ThenBy(item => item.name, StringComparer.Ordinal))
            {
                builder.Append("- `").Append(Markdown(change.target.path)).Append("` / `")
                    .Append(Markdown(change.name)).Append("`: `").Append(Number(change.baseline)).Append("` → `")
                    .Append(Number(change.value)).Append("`\n");
            }
            builder.Append("\n## Active State Changes\n\n");
            foreach (var change in state.activeStateChanges
                         .OrderBy(item => item.target.path, StringComparer.Ordinal))
            {
                builder.Append("- `").Append(Markdown(change.target.path)).Append("`: `")
                    .Append(change.baseline.ToString().ToLowerInvariant()).Append("` → `")
                    .Append(change.value.ToString().ToLowerInvariant()).Append("`\n");
            }
            builder.Append("\n## Warnings / Manual Review\n\n");
            foreach (var warning in (state.manualReview ?? new List<string>()).Distinct(StringComparer.Ordinal)
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                builder.Append("- ").Append(Markdown(warning)).Append("\n");
            }
            return builder.ToString();
        }

        public static string AppendHistory(string existingJson)
        {
            var events = new List<HistoryEvent>();
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                var history = JsonUtility.FromJson<HistoryFile>(existingJson);
                if (history == null || history.schemaVersion != 1)
                {
                    throw new InvalidDataException("history.json is invalid or uses an unsupported schema.");
                }
                if (history.events != null) events.AddRange(history.events);
            }
            events.Add(new HistoryEvent
            {
                type = "recipe_saved",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                recipeId = "avatar"
            });
            return JsonUtility.ToJson(new HistoryFile { schemaVersion = 1, events = events.ToArray() }, true) + "\n";
        }

        private static string Target(TargetLocator target)
        {
            return "{\"scope\": " + Json(target.scope) + ", \"path\": " + Json(target.path) +
                   (string.IsNullOrEmpty(target.componentId) ? "}" : ", \"componentId\": " + Json(target.componentId) + "}");
        }
        private static string Vector(Vector3Value value) => "[" + Number(value.x) + ", " + Number(value.y) + ", " + Number(value.z) + "]";
        private static string Quaternion(QuaternionValue value) => "[" + Number(value.x) + ", " + Number(value.y) + ", " + Number(value.z) + ", " + Number(value.w) + "]";
        private static string Number(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string Markdown(string value) => (value ?? string.Empty).Replace("`", "\\`").Replace("\r", " ").Replace("\n", " ");
        private static string Json(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4")); else builder.Append(c); break;
                }
            }
            return builder.Append('"').ToString();
        }
    }

    [Serializable]
    internal sealed class HistoryFile { public int schemaVersion; public HistoryEvent[] events; }
    [Serializable]
    internal sealed class HistoryEvent { public string type; public string timestamp; public string recipeId; }
}
