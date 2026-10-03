using System;
using System.Collections.Generic;
using System.Linq;
using AvatarRecipe.Editor.Core.Models;
using AvatarRecipe.Editor.Core.Snapshot;
using AvatarRecipe.Editor.Localization;

namespace AvatarRecipe.Editor.Core.Diff
{
    public static class AvatarDiffEngine
    {
        public static RecipeState Diff(AvatarSnapshot baseline, AvatarSnapshot current)
        {
            ValidateSnapshot(baseline, nameof(baseline));
            ValidateSnapshot(current, nameof(current));
            var baselinePrefabs = IndexPrefabs(baseline.addedPrefabs);
            var currentPrefabs = IndexPrefabs(current.addedPrefabs);
            var addedPrefabs = currentPrefabs.Values
                .Where(prefab => !baselinePrefabs.ContainsKey(GetPrefabIdentity(prefab)))
                .OrderBy(prefab => prefab.path, StringComparer.Ordinal)
                .ThenBy(prefab => prefab.sourceGuid, StringComparer.Ordinal)
                .ToList();
            var currentWithAddedPrefabs = current;
            current = ExcludeAddedPrefabSubtrees(current, addedPrefabs);
            var recipeState = new RecipeState();
            var baselineTransformPaths = new HashSet<string>(baseline.transforms.Select(item => item.path), StringComparer.Ordinal);
            AddModularAvatarChanges(recipeState, baseline.modularAvatarComponents,
                currentWithAddedPrefabs.modularAvatarComponents, baselineTransformPaths);

            var baselineTransformMap = IndexByPath(baseline.transforms, snapshot => snapshot.path, "Transform");
            var currentTransformMap = IndexByPath(current.transforms, snapshot => snapshot.path, "Transform");
            var commonTransformPaths = new HashSet<string>(baselineTransformMap.Keys, StringComparer.Ordinal);
            commonTransformPaths.IntersectWith(currentTransformMap.Keys);
            AddHierarchyReviewItems(recipeState, baselineTransformMap.Keys, currentTransformMap.Keys);

            var baselineBlendShapes = IndexBlendShapes(baseline.blendShapes);
            var currentBlendShapes = IndexBlendShapes(current.blendShapes);
            var baselineMaterials = IndexMaterials(baseline.materials);
            var currentMaterials = IndexMaterials(current.materials);
            AddBlendShapeReviewItems(recipeState, baselineBlendShapes, currentBlendShapes, commonTransformPaths);
            baseline = FilterSnapshot(baseline, commonTransformPaths,
                baselineBlendShapes.Keys.Intersect(currentBlendShapes.Keys).ToHashSet());
            current = FilterSnapshot(current, commonTransformPaths,
                currentBlendShapes.Keys.Intersect(baselineBlendShapes.Keys).ToHashSet());
            AddMaterialChanges(recipeState, baselineMaterials, currentMaterials, commonTransformPaths);

            foreach (var prefab in addedPrefabs)
            {
                var parentPath = GetParentPath(prefab.path);
                if (!baselineTransformMap.ContainsKey(parentPath) || !currentTransformMap.ContainsKey(parentPath))
                {
                    recipeState.manualReview.Add("Added Prefab parent is not present in the base Avatar: " +
                                                 DisplayPath(parentPath) + " (" + prefab.sourceName + ")");
                    continue;
                }

                var identity = GetPrefabIdentity(prefab);
                recipeState.addedPrefabs.Add(new AddedPrefabEntry
                {
                    id = CreateRecipePrefabId(identity),
                    name = prefab.sourceName,
                    guid = prefab.sourceGuid,
                    assetPath = prefab.sourceAssetPath,
                    parentScope = TargetLocator.BaseScope,
                    parentPath = parentPath,
                    siblingIndex = prefab.siblingIndex,
                    localPosition = prefab.localPosition,
                    localRotation = prefab.localRotation,
                    localScale = prefab.localScale
                });
            }
            recipeState.manualReview = recipeState.manualReview.Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal).ToList();
            var currentTransforms = IndexByPath(current.transforms, snapshot => snapshot.path, "Transform");
            foreach (var baselineTransform in SortByPath(baseline.transforms, snapshot => snapshot.path))
            {
                var currentTransform = currentTransforms[baselineTransform.path];
                AddVector3Change(recipeState, baselineTransform.path, "localPosition",
                    baselineTransform.localPosition, currentTransform.localPosition);
                AddQuaternionChange(recipeState, baselineTransform.path, "localRotation",
                    baselineTransform.localRotation, currentTransform.localRotation);
                AddVector3Change(recipeState, baselineTransform.path, "localScale",
                    baselineTransform.localScale, currentTransform.localScale);
            }

            var currentBlendShapeMap = IndexBlendShapes(current.blendShapes);
            foreach (var baselineBlendShape in SortBlendShapes(baseline.blendShapes))
            {
                var identity = new BlendShapeIdentity(baselineBlendShape);
                var currentBlendShape = currentBlendShapeMap[identity];
                var before = AvatarSnapshotBuilder.Normalize(baselineBlendShape.weight);
                var after = AvatarSnapshotBuilder.Normalize(currentBlendShape.weight);
                if (before.Equals(after))
                {
                    continue;
                }

                recipeState.blendShapeChanges.Add(new BlendShapeChange
                {
                    target = CopyTarget(baselineBlendShape.target),
                    name = baselineBlendShape.name,
                    baseline = before,
                    value = after
                });
            }

            var currentActiveStates = IndexByPath(current.activeStates, snapshot => snapshot.path, "Active state");
            foreach (var baselineActive in SortByPath(baseline.activeStates, snapshot => snapshot.path))
            {
                var currentActive = currentActiveStates[baselineActive.path];
                if (baselineActive.activeSelf == currentActive.activeSelf)
                {
                    continue;
                }

                recipeState.activeStateChanges.Add(new ActiveStateChange
                {
                    target = new TargetLocator
                    {
                        scope = TargetLocator.BaseScope,
                        path = baselineActive.path
                    },
                    baseline = baselineActive.activeSelf,
                    value = currentActive.activeSelf
                });
            }

            return recipeState;
        }

        private static void AddModularAvatarChanges(RecipeState state,
            IList<ModularAvatarComponentSnapshot> baselineEntries, IList<ModularAvatarComponentSnapshot> currentEntries,
            HashSet<string> baselineTransformPaths)
        {
            var baseline = (baselineEntries ?? new List<ModularAvatarComponentSnapshot>())
                .Where(item => item != null).ToDictionary(item => item.key, StringComparer.Ordinal);
            var current = (currentEntries ?? new List<ModularAvatarComponentSnapshot>())
                .Where(item => item != null).ToDictionary(item => item.key, StringComparer.Ordinal);

            foreach (var key in baseline.Keys.Union(current.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
            {
                baseline.TryGetValue(key, out var before);
                current.TryGetValue(key, out var after);
                if (before != null && after != null && ComponentsEqual(before, after)) continue;

                var change = new ModularAvatarChange
                {
                    operation = before == null ? "added" : after == null ? "removed" : "modified",
                    path = (after ?? before).path,
                    createHost = after != null && !baselineTransformPaths.Contains(after.path),
                    hasHostPlacement = after != null && before == null,
                    hostParentPath = (after ?? before).hostParentPath,
                    hostSiblingIndex = (after ?? before).hostSiblingIndex,
                    hostLocalPosition = (after ?? before).hostLocalPosition,
                    hostLocalRotation = (after ?? before).hostLocalRotation,
                    hostLocalScale = (after ?? before).hostLocalScale,
                    hostActive = (after ?? before).hostActive,
                    componentType = (after ?? before).componentType,
                    componentName = (after ?? before).componentName,
                    baselineEnabled = before != null && before.enabled,
                    valueEnabled = after != null && after.enabled
                };
                var beforeProperties = (before == null ? new List<ModularAvatarPropertySnapshot>() : before.properties ?? new List<ModularAvatarPropertySnapshot>())
                    .Where(item => item != null && AvatarSnapshotBuilder.IsUserFacingModularPropertyPath(item.path))
                    .ToDictionary(item => item.path, item => item.value, StringComparer.Ordinal);
                var afterProperties = (after == null ? new List<ModularAvatarPropertySnapshot>() : after.properties ?? new List<ModularAvatarPropertySnapshot>())
                    .Where(item => item != null && AvatarSnapshotBuilder.IsUserFacingModularPropertyPath(item.path))
                    .ToDictionary(item => item.path, item => item.value, StringComparer.Ordinal);
                foreach (var propertyPath in beforeProperties.Keys.Union(afterProperties.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
                {
                    beforeProperties.TryGetValue(propertyPath, out var oldValue);
                    afterProperties.TryGetValue(propertyPath, out var newValue);
                    if (beforeProperties.ContainsKey(propertyPath) == afterProperties.ContainsKey(propertyPath) &&
                        string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;
                    change.properties.Add(new ModularAvatarPropertyChange
                    {
                        path = propertyPath,
                        baselineExists = beforeProperties.ContainsKey(propertyPath),
                        baseline = oldValue ?? string.Empty,
                        valueExists = afterProperties.ContainsKey(propertyPath),
                        value = newValue ?? string.Empty
                    });
                }
                state.modularAvatarChanges.Add(change);
            }

            AddModularAvatarDisplayContext(state.modularAvatarChanges,
                baselineEntries ?? new List<ModularAvatarComponentSnapshot>(),
                currentEntries ?? new List<ModularAvatarComponentSnapshot>());

            if (state.modularAvatarChanges.Any(change => !IsSupportedModularChange(change, state.modularAvatarChanges)))
                state.manualReview.Add("Modular Avatar changes are recorded in modular-avatar.md and require manual review; they are not applied automatically.");
        }

        private static void AddModularAvatarDisplayContext(IList<ModularAvatarChange> changes,
            IList<ModularAvatarComponentSnapshot> baseline, IList<ModularAvatarComponentSnapshot> current)
        {
            foreach (var change in changes)
            {
                if (change.componentName != "ModularAvatarMenuItem" && change.componentName != "ModularAvatarObjectToggle")
                    continue;

                var menu = current.FirstOrDefault(item => item.path == change.path && item.componentName == "ModularAvatarMenuItem") ??
                           baseline.FirstOrDefault(item => item.path == change.path && item.componentName == "ModularAvatarMenuItem");
                if (menu != null)
                {
                    var label = SnapshotValue(menu, "label");
                    change.displayName = string.IsNullOrWhiteSpace(label) ? DisplayLeaf(change.path) : label;
                    change.displayType = EnumName(SnapshotValue(menu, "Control.type"));
                }

                var toggle = current.FirstOrDefault(item => item.path == change.path && item.componentName == "ModularAvatarObjectToggle") ??
                             baseline.FirstOrDefault(item => item.path == change.path && item.componentName == "ModularAvatarObjectToggle");
                if (toggle != null)
                    change.targets = SnapshotToggleTargets(toggle);
            }
        }

        private static List<ModularAvatarTarget> SnapshotToggleTargets(ModularAvatarComponentSnapshot toggle)
        {
            var properties = (toggle.properties ?? new List<ModularAvatarPropertySnapshot>())
                .Where(item => item != null).ToDictionary(item => item.path, item => item.value, StringComparer.Ordinal);
            var indices = properties.Keys.Where(path => path.StartsWith("m_objects.Array.data[", StringComparison.Ordinal) &&
                    path.EndsWith(".Object.referencePath", StringComparison.Ordinal))
                .Select(path => ArrayElementIndex(path, ".Object.referencePath"))
                .Where(index => index >= 0).Distinct().OrderBy(index => index);
            var result = new List<ModularAvatarTarget>();
            foreach (var index in indices)
            {
                var prefix = "m_objects.Array.data[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                var targetPath = SnapshotValue(properties, prefix + ".Object.referencePath");
                if (string.IsNullOrEmpty(targetPath)) continue;
                var activeValue = SnapshotValue(properties, prefix + ".Active");
                result.Add(new ModularAvatarTarget
                {
                    path = targetPath,
                    activeWhenEnabled = !string.Equals(activeValue, "false", StringComparison.OrdinalIgnoreCase)
                });
            }
            return result;
        }

        private static int ArrayElementIndex(string propertyPath, string suffix)
        {
            const string prefix = "m_objects.Array.data[";
            var start = prefix.Length;
            var end = propertyPath.IndexOf(']', start);
            return end < 0 || !propertyPath.EndsWith(suffix, StringComparison.Ordinal) ||
                   !int.TryParse(propertyPath.Substring(start, end - start), out var index) ? -1 : index;
        }

        private static string SnapshotValue(ModularAvatarComponentSnapshot component, string path)
        {
            var property = (component.properties ?? new List<ModularAvatarPropertySnapshot>())
                .FirstOrDefault(item => item != null && item.path == path);
            return property == null ? string.Empty : property.value ?? string.Empty;
        }

        private static string SnapshotValue(IDictionary<string, string> properties, string path) =>
            properties.TryGetValue(path, out var value) ? value ?? string.Empty : string.Empty;

        private static string EnumName(string serializedEnum)
        {
            if (string.IsNullOrEmpty(serializedEnum)) return string.Empty;
            var colon = serializedEnum.IndexOf(':');
            return colon < 0 ? serializedEnum : serializedEnum.Substring(colon + 1);
        }

        private static string DisplayLeaf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Avatar";
            var separator = path.LastIndexOf('/');
            var leaf = separator < 0 ? path : path.Substring(separator + 1);
            try { return Uri.UnescapeDataString(leaf); }
            catch (UriFormatException) { return leaf; }
        }

        private static bool ComponentsEqual(ModularAvatarComponentSnapshot left, ModularAvatarComponentSnapshot right)
        {
            if (left.enabled != right.enabled) return false;
            var leftProperties = left.properties ?? new List<ModularAvatarPropertySnapshot>();
            var rightProperties = right.properties ?? new List<ModularAvatarPropertySnapshot>();
            if (leftProperties.Count != rightProperties.Count) return false;
            for (var index = 0; index < leftProperties.Count; index++)
                if (leftProperties[index].path != rightProperties[index].path || leftProperties[index].value != rightProperties[index].value)
                    return false;
            return true;
        }

        private static void ValidateSnapshot(AvatarSnapshot snapshot, string parameterName)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (snapshot.schemaVersion != AvatarSnapshot.CurrentSchemaVersion)
            {
                throw new ArgumentException("Unsupported AvatarSnapshot schema version: " + snapshot.schemaVersion,
                    parameterName);
            }

            if (snapshot.transforms == null || snapshot.blendShapes == null || snapshot.activeStates == null || snapshot.materials == null)
            {
                throw new ArgumentException("AvatarSnapshot contains a missing collection.", parameterName);
            }
        }

        private static Dictionary<string, AddedPrefabSnapshot> IndexPrefabs(List<AddedPrefabSnapshot> prefabs)
        {
            var result = new Dictionary<string, AddedPrefabSnapshot>(StringComparer.Ordinal);
            if (prefabs == null)
            {
                return result;
            }

            foreach (var prefab in prefabs)
            {
                if (prefab == null || string.IsNullOrEmpty(prefab.globalObjectId) ||
                    string.IsNullOrEmpty(prefab.path) || string.IsNullOrEmpty(prefab.sourceGuid) ||
                    string.IsNullOrEmpty(prefab.sourceAssetPath) ||
                    !result.TryAdd(GetPrefabIdentity(prefab), prefab))
                {
                    throw new ArgumentException("AvatarSnapshot contains an invalid or duplicate Prefab identity.");
                }
            }
            return result;
        }

        private static AvatarSnapshot ExcludeAddedPrefabSubtrees(AvatarSnapshot snapshot,
            List<AddedPrefabSnapshot> addedPrefabs)
        {
            if (addedPrefabs.Count == 0)
            {
                return snapshot;
            }

            var addedPrefabPaths = new HashSet<string>(
                addedPrefabs.Select(prefab => prefab.path), StringComparer.Ordinal);

            bool IsWithinAddedPrefab(string path)
            {
                if (addedPrefabPaths.Contains(path)) return true;
                for (var separator = path.LastIndexOf('/'); separator >= 0;
                     separator = separator == 0 ? -1 : path.LastIndexOf('/', separator - 1))
                {
                    if (addedPrefabPaths.Contains(path.Substring(0, separator))) return true;
                }
                return false;
            }

            return new AvatarSnapshot
            {
                schemaVersion = snapshot.schemaVersion,
                transforms = snapshot.transforms.Where(item => !IsWithinAddedPrefab(item.path)).ToList(),
                blendShapes = snapshot.blendShapes.Where(item => !IsWithinAddedPrefab(item.target.path)).ToList(),
                materials = snapshot.materials.Where(item => !IsWithinAddedPrefab(item.target.path)).ToList(),
                activeStates = snapshot.activeStates.Where(item => !IsWithinAddedPrefab(item.path)).ToList(),
                addedPrefabs = snapshot.addedPrefabs
            };
        }

        private static string GetParentPath(string path)
        {
            var separatorIndex = path.LastIndexOf('/');
            return separatorIndex < 0 ? string.Empty : path.Substring(0, separatorIndex);
        }

        private static string CreateRecipePrefabId(string stableIdentity)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stableIdentity));
                var builder = new System.Text.StringBuilder("prefab-");
                for (var index = 0; index < 8; index++)
                {
                    builder.Append(bytes[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static AvatarSnapshot FilterSnapshot(AvatarSnapshot snapshot, HashSet<string> commonPaths,
            HashSet<BlendShapeIdentity> commonBlendShapes)
        {
            return new AvatarSnapshot
            {
                schemaVersion = snapshot.schemaVersion,
                transforms = snapshot.transforms.Where(item => commonPaths.Contains(item.path)).ToList(),
                activeStates = snapshot.activeStates.Where(item => commonPaths.Contains(item.path)).ToList(),
                blendShapes = snapshot.blendShapes.Where(item => commonBlendShapes.Contains(new BlendShapeIdentity(item))).ToList(),
                materials = snapshot.materials.Where(item => commonPaths.Contains(item.target.path)).ToList(),
                addedPrefabs = snapshot.addedPrefabs
            };
        }

        private static void AddHierarchyReviewItems(RecipeState state, ICollection<string> baselinePaths,
            ICollection<string> currentPaths)
        {
            var baseline = new HashSet<string>(baselinePaths, StringComparer.Ordinal);
            var current = new HashSet<string>(currentPaths, StringComparer.Ordinal);
            AddPathReviewItems(state, baseline.Except(current), true, null);
            var supportedHosts = new HashSet<string>((state.modularAvatarChanges ?? new List<ModularAvatarChange>())
                .Where(change => change.operation == "added" && change.hasHostPlacement &&
                    IsSupportedModularChange(change, state.modularAvatarChanges))
                .Select(change => change.path), StringComparer.Ordinal);
            AddPathReviewItems(state, current.Except(baseline), false, supportedHosts);
        }

        private static void AddPathReviewItems(RecipeState state, IEnumerable<string> paths, bool removed, HashSet<string> supportedHosts)
        {
            var candidates = paths.OrderBy(path => path.Count(character => character == '/'))
                .ThenBy(path => path, StringComparer.Ordinal).ToList();
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in candidates)
            {
                var coveredByReportedRoot = false;
                for (var separator = path.LastIndexOf('/'); separator >= 0;
                     separator = separator == 0 ? -1 : path.LastIndexOf('/', separator - 1))
                {
                    if (!roots.Contains(path.Substring(0, separator))) continue;
                    coveredByReportedRoot = true;
                    break;
                }
                if (coveredByReportedRoot) continue;
                if (!removed && supportedHosts != null && supportedHosts.Contains(path)) continue;
                roots.Add(path);
                state.manualReview.Add(removed
                    ? "Removed base object requires manual review: " + DisplayPath(path)
                    : "Added object has no source Prefab; automatic restore is unsupported: " + DisplayPath(path));
            }
        }

        private static bool IsSupportedModularChange(ModularAvatarChange change, IList<ModularAvatarChange> changes)
        {
            if (change == null || change.operation != "added" || !change.hasHostPlacement) return false;
            if (change.componentName != "ModularAvatarMenuInstaller" && change.componentName != "ModularAvatarMenuItem" &&
                change.componentName != "ModularAvatarObjectToggle") return false;
            return changes.Any(item => item != null && item.operation == "added" && item.path == change.path &&
                item.componentName == "ModularAvatarMenuItem") &&
                changes.Any(item => item != null && item.operation == "added" && item.path == change.path &&
                item.componentName == "ModularAvatarObjectToggle");
        }

        private static void AddBlendShapeReviewItems(RecipeState state,
            Dictionary<BlendShapeIdentity, BlendShapeSnapshot> baseline,
            Dictionary<BlendShapeIdentity, BlendShapeSnapshot> current,
            HashSet<string> commonTransformPaths)
        {
            foreach (var key in current.Keys.Except(baseline.Keys)
                         .Where(key => commonTransformPaths.Contains(key.Path))
                         .OrderBy(key => key.Path, StringComparer.Ordinal).ThenBy(key => key.ComponentId, StringComparer.Ordinal)
                         .ThenBy(key => key.Name, StringComparer.Ordinal))
            {
                state.manualReview.Add("Added BlendShape is not included in MVP reconstruction: " + key.Path + "/" + key.Name);
            }
            foreach (var key in baseline.Keys.Except(current.Keys)
                         .Where(key => commonTransformPaths.Contains(key.Path))
                         .OrderBy(key => key.Path, StringComparer.Ordinal).ThenBy(key => key.ComponentId, StringComparer.Ordinal)
                         .ThenBy(key => key.Name, StringComparer.Ordinal))
            {
                state.manualReview.Add("Removed BlendShape requires manual review: " + key.Path + "/" + key.Name);
            }
        }

        private static string GetPrefabIdentity(AddedPrefabSnapshot prefab)
        {
            return prefab.sourceGuid + "\n" + prefab.path;
        }

        private static string DisplayPath(string path)
        {
            return string.IsNullOrEmpty(path) ? "<Avatar Root>" : path;
        }

        private static Dictionary<string, T> IndexByPath<T>(IEnumerable<T> items, Func<T, string> pathSelector, string kind)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (ReferenceEquals(item, null))
                {
                    throw new ArgumentException("AvatarSnapshot contains a null " + kind + " entry.");
                }

                var path = pathSelector(item);
                if (path == null || !result.TryAdd(path, item))
                {
                    throw new ArgumentException("AvatarSnapshot contains an invalid or duplicate " + kind + " path.");
                }
            }

            return result;
        }

        private static Dictionary<BlendShapeIdentity, BlendShapeSnapshot> IndexBlendShapes(
            IEnumerable<BlendShapeSnapshot> blendShapes)
        {
            var result = new Dictionary<BlendShapeIdentity, BlendShapeSnapshot>();
            foreach (var blendShape in blendShapes)
            {
                if (blendShape == null || blendShape.target == null || blendShape.target.path == null ||
                    blendShape.target.scope != TargetLocator.BaseScope ||
                    string.IsNullOrEmpty(blendShape.target.componentId) || blendShape.name == null ||
                    !result.TryAdd(new BlendShapeIdentity(blendShape), blendShape))
                {
                    throw new ArgumentException("AvatarSnapshot contains an invalid or duplicate BlendShape identity.");
                }
            }

            return result;
        }

        private static Dictionary<string, MaterialSlotSnapshot> IndexMaterials(IEnumerable<MaterialSlotSnapshot> materials)
        {
            var result = new Dictionary<string, MaterialSlotSnapshot>(StringComparer.Ordinal);
            foreach (var material in materials)
            {
                if (material == null || material.target == null || material.target.scope != TargetLocator.BaseScope ||
                    material.target.path == null || string.IsNullOrEmpty(material.target.componentId) || material.materialIndex < 0 ||
                    !result.TryAdd(MaterialSlotKey(material.target, material.materialIndex), material))
                    throw new ArgumentException("AvatarSnapshot contains an invalid or duplicate material slot.");
            }
            return result;
        }

        private static void AddMaterialChanges(RecipeState state,
            Dictionary<string, MaterialSlotSnapshot> baseline, Dictionary<string, MaterialSlotSnapshot> current,
            HashSet<string> commonPaths)
        {
            var keys = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);
            keys.UnionWith(current.Keys);
            foreach (var key in keys.OrderBy(item => item, StringComparer.Ordinal))
            {
                baseline.TryGetValue(key, out var before);
                current.TryGetValue(key, out var after);
                var target = before != null ? before.target : after.target;
                var materialIndex = before != null ? before.materialIndex : after.materialIndex;
                if (!commonPaths.Contains(target.path)) continue;

                var assignmentChanged = before == null || after == null || before.hasMaterial != after.hasMaterial ||
                    !SameAsset(before.material, after.material);
                if (assignmentChanged)
                {
                    if (before != null && before.hasMaterial && before.material == null ||
                        after != null && after.hasMaterial && after.material == null)
                    {
                        state.manualReview.Add("Material is not a persistent project asset and cannot be restored automatically: " +
                                               target.path + " slot " + materialIndex);
                        continue;
                    }
                    var duplicateAssignmentProperties = new List<string>();
                    var sameShader = before != null && after != null &&
                        before.hasShader == after.hasShader && SameAsset(before.shader, after.shader);
                    var assignmentPropertyChanges = sameShader
                        ? DiffMaterialProperties(before.properties, after.properties, out duplicateAssignmentProperties)
                        : new List<MaterialPropertyChange>();
                    var materialState = new List<MaterialPropertySnapshot>();
                    if (after != null && after.hasMaterial)
                    {
                        var stateDuplicates = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var property in after.properties ?? new List<MaterialPropertySnapshot>())
                        {
                            if (!stateDuplicates.Add(property.name)) continue;
                            materialState.Add(CopyProperty(property));
                        }
                        foreach (var propertyName in (after.properties ?? new List<MaterialPropertySnapshot>())
                                     .GroupBy(item => item.name, StringComparer.Ordinal)
                                     .Where(group => group.Count() > 1 && !string.Equals(group.Key, "_DummyProperty", StringComparison.Ordinal))
                                     .Select(group => group.Key))
                            state.manualReview.Add(AvatarRecipeLocalization.Format(
                                "Shader declares duplicate Material property name, skipped: {0} at {1} (slot {2})",
                                propertyName, target.path, materialIndex));
                        materialState.RemoveAll(item => string.Equals(item.name, "_DummyProperty", StringComparison.Ordinal));
                    }
                    if (before != null && after != null)
                    {
                        foreach (var propertyName in duplicateAssignmentProperties)
                            state.manualReview.Add(AvatarRecipeLocalization.Format(
                                "Shader declares duplicate Material property name, skipped: {0} at {1} (slot {2})",
                                propertyName, target.path, materialIndex));
                    }
                    state.materialChanges.Add(new MaterialChange
                    {
                        target = CopyTarget(target),
                        materialIndex = materialIndex,
                        baselineMaterial = CopyAsset(before == null ? null : before.material),
                        valueMaterial = CopyAsset(after == null ? null : after.material),
                        baselineShader = CopyAsset(before == null ? null : before.shader),
                        valueShader = CopyAsset(after == null ? null : after.shader),
                        baselineRenderQueue = before == null ? 0 : before.renderQueue,
                        valueRenderQueue = after == null ? 0 : after.renderQueue,
                        baselineShaderKeywords = before == null ? Array.Empty<string>() : (before.shaderKeywords ?? Array.Empty<string>()).ToArray(),
                        valueShaderKeywords = after == null ? Array.Empty<string>() : (after.shaderKeywords ?? Array.Empty<string>()).ToArray(),
                        properties = assignmentPropertyChanges,
                        valueMaterialState = materialState
                    });
                    continue;
                }

                if (before == null || after == null || !before.hasMaterial) continue;
                var shaderChanged = before.hasShader != after.hasShader || !SameAsset(before.shader, after.shader);
                List<MaterialPropertyChange> propertyChanges;
                List<string> duplicateProperties;
                if (shaderChanged)
                {
                    propertyChanges = new List<MaterialPropertyChange>();
                    duplicateProperties = new List<string>();
                }
                else
                {
                    propertyChanges = DiffMaterialProperties(before.properties, after.properties, out duplicateProperties);
                }
                foreach (var propertyName in duplicateProperties)
                {
                    state.manualReview.Add(AvatarRecipeLocalization.Format(
                        "Shader declares duplicate Material property name, skipped: {0} at {1} (slot {2})",
                        propertyName, target.path, materialIndex));
                }
                var materialSettingsChanged = before.renderQueue != after.renderQueue ||
                    !(before.shaderKeywords ?? Array.Empty<string>()).SequenceEqual(after.shaderKeywords ?? Array.Empty<string>(), StringComparer.Ordinal);
                if (!shaderChanged && propertyChanges.Count == 0 && !materialSettingsChanged) continue;
                if (before.material == null || (before.hasShader && before.shader == null) || (after.hasShader && after.shader == null))
                {
                    state.manualReview.Add("Material or Shader is not a persistent project asset and cannot be restored automatically: " +
                                           target.path + " slot " + materialIndex);
                    continue;
                }
                if (propertyChanges.Any(change => change.type == "texture" &&
                        (change.baselineExists && change.baseline.hasTexture && change.baseline.texture == null ||
                         change.valueExists && change.value.hasTexture && change.value.texture == null)))
                {
                    state.manualReview.Add("Material uses a non-asset texture that cannot be restored automatically: " +
                                           target.path + " slot " + materialIndex);
                    continue;
                }

                state.materialChanges.Add(new MaterialChange
                {
                    target = CopyTarget(target),
                    materialIndex = materialIndex,
                    baselineMaterial = CopyAsset(before.material),
                    valueMaterial = CopyAsset(after.material),
                    baselineShader = CopyAsset(before.shader),
                    valueShader = CopyAsset(after.shader),
                    baselineRenderQueue = before.renderQueue,
                    valueRenderQueue = after.renderQueue,
                    baselineShaderKeywords = (before.shaderKeywords ?? Array.Empty<string>()).ToArray(),
                    valueShaderKeywords = (after.shaderKeywords ?? Array.Empty<string>()).ToArray(),
                    properties = propertyChanges
                });
            }
        }

        private static List<MaterialPropertyChange> DiffMaterialProperties(
            List<MaterialPropertySnapshot> baseline, List<MaterialPropertySnapshot> current, out List<string> duplicateNames)
        {
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var before = IndexMaterialProperties(baseline, duplicates);
            var after = IndexMaterialProperties(current, duplicates);
            var allDuplicateNames = duplicates.OrderBy(item => item, StringComparer.Ordinal).ToList();
            // lilToon deliberately repeats this diagnostic placeholder to display different
            // recovery messages. It is not an editable Material value and should not become a warning.
            duplicateNames = allDuplicateNames
                .Where(item => !string.Equals(item, "_DummyProperty", StringComparison.Ordinal))
                .ToList();
            foreach (var duplicateName in allDuplicateNames)
            {
                // ShaderUtil can expose repeated declarations for malformed or generated shaders.
                // A name-only Material API cannot address these safely, so leave them for review.
                before.Remove(duplicateName);
                after.Remove(duplicateName);
            }
            var result = new List<MaterialPropertyChange>();
            var names = new HashSet<string>(before.Keys, StringComparer.Ordinal);
            names.UnionWith(after.Keys);
            foreach (var name in names.OrderBy(item => item, StringComparer.Ordinal))
            {
                before.TryGetValue(name, out var oldValue);
                after.TryGetValue(name, out var newValue);
                if (oldValue != null && newValue != null && SamePropertyValue(oldValue, newValue)) continue;
                result.Add(new MaterialPropertyChange
                {
                    name = name,
                    type = newValue != null ? newValue.type : oldValue.type,
                    baselineExists = oldValue != null,
                    valueExists = newValue != null,
                    baseline = CopyProperty(oldValue),
                    value = CopyProperty(newValue)
                });
            }
            return result;
        }

        private static Dictionary<string, MaterialPropertySnapshot> IndexMaterialProperties(
            IEnumerable<MaterialPropertySnapshot> properties, HashSet<string> duplicateNames)
        {
            var result = new Dictionary<string, MaterialPropertySnapshot>(StringComparer.Ordinal);
            foreach (var property in properties ?? Enumerable.Empty<MaterialPropertySnapshot>())
            {
                if (property == null || string.IsNullOrEmpty(property.name))
                    throw new ArgumentException("AvatarSnapshot contains an invalid Material property.");
                if (!result.TryAdd(property.name, property)) duplicateNames.Add(property.name);
            }
            return result;
        }

        private static bool SamePropertyValue(MaterialPropertySnapshot left, MaterialPropertySnapshot right)
        {
            if (left.type != right.type) return false;
            switch (left.type)
            {
                case "float": return Approximately(left.floatValue, right.floatValue);
                case "color":
                case "vector": return left.vectorValue.Equals(right.vectorValue);
                case "texture": return left.hasTexture == right.hasTexture && SameAsset(left.texture, right.texture) &&
                    left.textureScale.Equals(right.textureScale) && left.textureOffset.Equals(right.textureOffset);
                default: return false;
            }
        }

        private static bool Approximately(float left, float right) => Math.Abs(left - right) <= 0.00001f;

        private static string MaterialSlotKey(TargetLocator target, int materialIndex) =>
            target.path + "\n" + target.componentId + "\n" + materialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static bool SameAsset(AssetReference left, AssetReference right)
        {
            var leftIsEmpty = IsEmptyAssetReference(left);
            var rightIsEmpty = IsEmptyAssetReference(right);
            if (leftIsEmpty || rightIsEmpty) return leftIsEmpty && rightIsEmpty;
            if (!string.IsNullOrEmpty(left.guid) && !string.IsNullOrEmpty(right.guid))
                return string.Equals(left.guid, right.guid, StringComparison.Ordinal);
            return string.Equals(NormalizeAssetPath(left.assetPath), NormalizeAssetPath(right.assetPath), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmptyAssetReference(AssetReference asset) => asset == null ||
            string.IsNullOrEmpty(asset.guid) && string.IsNullOrEmpty(asset.assetPath);

        private static string NormalizeAssetPath(string path) => (path ?? string.Empty).Replace('\\', '/');

        private static AssetReference CopyAsset(AssetReference value) => value == null ? null : new AssetReference
        {
            name = value.name,
            guid = value.guid,
            assetPath = value.assetPath
        };

        private static MaterialPropertySnapshot CopyProperty(MaterialPropertySnapshot value) => value == null ? null : new MaterialPropertySnapshot
        {
            name = value.name,
            type = value.type,
            floatValue = value.floatValue,
            vectorValue = value.vectorValue,
            hasTexture = value.hasTexture,
            texture = CopyAsset(value.texture),
            textureScale = value.textureScale,
            textureOffset = value.textureOffset
        };

        private static List<T> SortByPath<T>(List<T> items, Func<T, string> pathSelector)
        {
            var sorted = new List<T>(items);
            sorted.Sort((left, right) => StringComparer.Ordinal.Compare(pathSelector(left), pathSelector(right)));
            return sorted;
        }

        private static List<BlendShapeSnapshot> SortBlendShapes(List<BlendShapeSnapshot> items)
        {
            return items
                .OrderBy(item => item.target.path, StringComparer.Ordinal)
                .ThenBy(item => item.target.componentId, StringComparer.Ordinal)
                .ThenBy(item => item.name, StringComparer.Ordinal)
                .ToList();
        }

        private static void AddVector3Change(RecipeState recipeState, string path, string property,
            Vector3Value baseline, Vector3Value current)
        {
            baseline = new Vector3Value(
                AvatarSnapshotBuilder.Normalize(baseline.x),
                AvatarSnapshotBuilder.Normalize(baseline.y),
                AvatarSnapshotBuilder.Normalize(baseline.z));
            current = new Vector3Value(
                AvatarSnapshotBuilder.Normalize(current.x),
                AvatarSnapshotBuilder.Normalize(current.y),
                AvatarSnapshotBuilder.Normalize(current.z));
            if (baseline.Equals(current))
            {
                return;
            }

            recipeState.transformChanges.Add(new TransformChange
            {
                target = BaseTarget(path),
                property = property,
                baselineVector3 = baseline,
                valueVector3 = current
            });
        }

        private static void AddQuaternionChange(RecipeState recipeState, string path, string property,
            QuaternionValue baseline, QuaternionValue current)
        {
            baseline = AvatarSnapshotBuilder.Normalize(new UnityEngine.Quaternion(
                baseline.x, baseline.y, baseline.z, baseline.w));
            current = AvatarSnapshotBuilder.Normalize(new UnityEngine.Quaternion(
                current.x, current.y, current.z, current.w));
            if (baseline.Equals(current))
            {
                return;
            }

            recipeState.transformChanges.Add(new TransformChange
            {
                target = BaseTarget(path),
                property = property,
                baselineQuaternion = baseline,
                valueQuaternion = current
            });
        }

        private static TargetLocator BaseTarget(string path)
        {
            return new TargetLocator { scope = TargetLocator.BaseScope, path = path };
        }

        private static TargetLocator CopyTarget(TargetLocator target)
        {
            return new TargetLocator
            {
                scope = target.scope,
                path = target.path,
                componentId = target.componentId
            };
        }

        private readonly struct BlendShapeIdentity : IEquatable<BlendShapeIdentity>
        {
            private readonly string _path;
            private readonly string _componentId;
            private readonly string _name;

            public BlendShapeIdentity(BlendShapeSnapshot snapshot)
            {
                _path = snapshot.target.path;
                _componentId = snapshot.target.componentId;
                _name = snapshot.name;
            }

            public string Path => _path;
            public string ComponentId => _componentId;
            public string Name => _name;

            public bool Equals(BlendShapeIdentity other)
            {
                return string.Equals(_path, other._path, StringComparison.Ordinal) &&
                       string.Equals(_componentId, other._componentId, StringComparison.Ordinal) &&
                       string.Equals(_name, other._name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is BlendShapeIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = StringComparer.Ordinal.GetHashCode(_path);
                    hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(_componentId);
                    return (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(_name);
                }
            }
        }
    }
}
