using System;
using System.Collections.Generic;
using System.Linq;
using AvatarRecipe.Editor.Core.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarRecipe.Editor.Apply
{
    internal sealed class ApplyResult
    {
        public int appliedOperations;
        public int skippedConflicts;
        public int manualReviewItems;
    }

    internal static class RecipeApplier
    {
        private const float Tolerance = 0.0001f;

        private sealed class PreparedPrefab
        {
            public AddedPrefabEntry entry;
            public GameObject asset;
            public Transform parent;
        }

        private sealed class PreparedTransform
        {
            public TransformChange change;
            public Transform target;
        }

        private sealed class PreparedActive
        {
            public ActiveStateChange change;
            public Transform target;
        }

        private sealed class PreparedBlendShape
        {
            public BlendShapeChange change;
            public SkinnedMeshRenderer renderer;
            public int index;
        }

        public static ApplyResult Apply(LoadedRecipe recipe, GameObject target, ApplyPlan plan, bool skipConflicts)
        {
            if (recipe == null || recipe.state == null) throw new ArgumentNullException(nameof(recipe));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (EditorSceneManager.IsPreviewScene(target.scene) || !target.scene.IsValid() || !target.scene.isLoaded)
                throw new InvalidOperationException("Target Avatar must belong to a loaded scene.");
            if (plan.compatibility.Any(item => item.status == CompatibilityStatus.Missing || item.status == CompatibilityStatus.Ambiguous))
                throw new InvalidOperationException("Apply is blocked while targets or Prefabs are missing or ambiguous. Resolve them and build a new preview.");

            var conflicts = new HashSet<string>(plan.compatibility
                .Where(item => item.status == CompatibilityStatus.Conflict)
                .Select(item => item.changeKey ?? string.Empty), StringComparer.Ordinal);
            var skipAllBaseOperations = skipConflicts && conflicts.Contains("base");
            var result = new ApplyResult
            {
                manualReviewItems = plan.compatibility.Count(item => item.status == CompatibilityStatus.ManualReview)
            };
            var prefabs = new List<PreparedPrefab>();
            var transforms = new List<PreparedTransform>();
            var activeStates = new List<PreparedActive>();
            var blendShapes = new List<PreparedBlendShape>();

            if (!skipAllBaseOperations)
            {
                foreach (var prefab in recipe.state.addedPrefabs)
                {
                    if (IsManualReviewPrefab(recipe, prefab, plan)) continue;
                    if (skipConflicts && conflicts.Contains("base")) { result.skippedConflicts++; continue; }
                    var asset = ApplyPlanner.ResolvePrefab(prefab, out var status, out _);
                    if (asset == null || status != CompatibilityStatus.Found)
                        throw new InvalidOperationException("A required Prefab changed after preview. Build a new preview before applying.");
                    var parent = ApplyPlanner.ResolvePath(target.transform, prefab.parentPath, out var ambiguous);
                    if (parent == null || ambiguous)
                        throw new InvalidOperationException("A Prefab parent changed after preview: " + prefab.parentPath);
                    prefabs.Add(new PreparedPrefab { entry = prefab, asset = asset, parent = parent });
                }

                foreach (var change in recipe.state.transformChanges)
                {
                    var key = TransformKey(change);
                    if (skipConflicts && conflicts.Contains(key)) { result.skippedConflicts++; continue; }
                    var resolved = ApplyPlanner.ResolvePath(target.transform, change.target.path, out var ambiguous);
                    if (resolved == null || ambiguous) throw ChangedTarget(change.target.path);
                    transforms.Add(new PreparedTransform { change = change, target = resolved });
                }
                foreach (var change in recipe.state.activeStateChanges)
                {
                    var key = ActiveKey(change);
                    if (skipConflicts && conflicts.Contains(key)) { result.skippedConflicts++; continue; }
                    var resolved = ApplyPlanner.ResolvePath(target.transform, change.target.path, out var ambiguous);
                    if (resolved == null || ambiguous) throw ChangedTarget(change.target.path);
                    activeStates.Add(new PreparedActive { change = change, target = resolved });
                }
                foreach (var change in recipe.state.blendShapeChanges)
                {
                    var key = BlendShapeKey(change);
                    if (skipConflicts && conflicts.Contains(key)) { result.skippedConflicts++; continue; }
                    var resolved = ApplyPlanner.ResolvePath(target.transform, change.target.path, out var ambiguous);
                    if (resolved == null || ambiguous) throw ChangedTarget(change.target.path);
                    var renderer = ResolveBlendShape(resolved, change, out var index);
                    if (renderer == null) throw ChangedTarget(change.target.path + "/" + change.name);
                    blendShapes.Add(new PreparedBlendShape { change = change, renderer = renderer, index = index });
                }
            }
            else
            {
                result.skippedConflicts = plan.operations.Count;
            }

            prefabs = prefabs.OrderBy(item => item.entry.parentPath, StringComparer.Ordinal)
                .ThenBy(item => item.entry.siblingIndex).ThenBy(item => item.entry.id, StringComparer.Ordinal).ToList();
            var createdPrefabs = new List<GameObject>();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Avatar Recipe");
            try
            {
                foreach (var item in prefabs)
                {
                    var instance = PrefabUtility.InstantiatePrefab(item.asset, target.scene) as GameObject;
                    if (instance == null) throw new InvalidOperationException("Unity failed to instantiate Prefab: " + item.entry.name);
                    Undo.RegisterCreatedObjectUndo(instance, "Apply Avatar Recipe");
                    Undo.SetTransformParent(instance.transform, item.parent, false, "Apply Avatar Recipe");
                    instance.transform.localPosition = ToUnity(item.entry.localPosition);
                    instance.transform.localRotation = ToUnity(item.entry.localRotation);
                    instance.transform.localScale = ToUnity(item.entry.localScale);
                    instance.transform.SetSiblingIndex(Mathf.Clamp(item.entry.siblingIndex, 0, item.parent.childCount - 1));
                    createdPrefabs.Add(instance);
                }

                foreach (var item in transforms)
                {
                    Undo.RecordObject(item.target, "Apply Avatar Recipe");
                    switch (item.change.property)
                    {
                        case "localPosition": item.target.localPosition = ToUnity(item.change.valueVector3); break;
                        case "localRotation": item.target.localRotation = ToUnity(item.change.valueQuaternion); break;
                        case "localScale": item.target.localScale = ToUnity(item.change.valueVector3); break;
                        default: throw new InvalidOperationException("Unsupported Transform property: " + item.change.property);
                    }
                }

                foreach (var item in activeStates)
                {
                    Undo.RecordObject(item.target.gameObject, "Apply Avatar Recipe");
                    item.target.gameObject.SetActive(item.change.value);
                }

                foreach (var item in blendShapes)
                {
                    Undo.RecordObject(item.renderer, "Apply Avatar Recipe");
                    item.renderer.SetBlendShapeWeight(item.index, item.change.value);
                }

                ValidateResults(prefabs, createdPrefabs, transforms, activeStates, blendShapes);
                EditorSceneManager.MarkSceneDirty(target.scene);
                Undo.CollapseUndoOperations(undoGroup);
                result.appliedOperations = prefabs.Count + transforms.Count + activeStates.Count + blendShapes.Count;
                return result;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw new InvalidOperationException("Apply failed. The scene changes were reverted: " + exception.Message, exception);
            }
        }

        private static bool IsManualReviewPrefab(LoadedRecipe recipe, AddedPrefabEntry prefab, ApplyPlan plan)
        {
            var isBasePrefab = (!string.IsNullOrEmpty(recipe.baseAvatar.prefabGuid) && prefab.guid == recipe.baseAvatar.prefabGuid) ||
                (!string.IsNullOrEmpty(recipe.baseAvatar.assetPath) && string.Equals(
                    prefab.assetPath.Replace('\\', '/'), recipe.baseAvatar.assetPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (isBasePrefab) return true;
            return plan.compatibility.Any(item => item.category == "Required Prefab" &&
                item.status == CompatibilityStatus.ManualReview && item.targetPath == prefab.parentPath &&
                item.message.StartsWith(prefab.name, StringComparison.Ordinal));
        }

        private static SkinnedMeshRenderer ResolveBlendShape(Transform target, BlendShapeChange change, out int shapeIndex)
        {
            shapeIndex = -1;
            const string prefix = "SkinnedMeshRenderer:";
            if (change.target.componentId == null || !change.target.componentId.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(change.target.componentId.Substring(prefix.Length), out var rendererIndex)) return null;
            var renderers = target.GetComponents<SkinnedMeshRenderer>();
            if (rendererIndex < 0 || rendererIndex >= renderers.Length || renderers[rendererIndex] == null ||
                renderers[rendererIndex].sharedMesh == null) return null;
            var renderer = renderers[rendererIndex];
            for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
            {
                if (!string.Equals(renderer.sharedMesh.GetBlendShapeName(i), change.name, StringComparison.Ordinal)) continue;
                if (shapeIndex >= 0) return null;
                shapeIndex = i;
            }
            return shapeIndex < 0 ? null : renderer;
        }

        private static void ValidateResults(List<PreparedPrefab> prefabs, List<GameObject> instances,
            List<PreparedTransform> transforms, List<PreparedActive> activeStates, List<PreparedBlendShape> blendShapes)
        {
            for (var i = 0; i < prefabs.Count; i++)
            {
                var expected = prefabs[i];
                var instance = instances[i];
                if (instance == null || instance.transform.parent != expected.parent ||
                    instance.transform.GetSiblingIndex() != Mathf.Clamp(expected.entry.siblingIndex, 0, expected.parent.childCount - 1) ||
                    !Approximately(instance.transform.localPosition, expected.entry.localPosition) ||
                    !Approximately(instance.transform.localRotation, expected.entry.localRotation) ||
                    !Approximately(instance.transform.localScale, expected.entry.localScale))
                    throw new InvalidOperationException("Final Prefab validation failed: " + expected.entry.name);
            }
            foreach (var item in transforms)
            {
                var valid = item.change.property == "localPosition"
                    ? Approximately(item.target.localPosition, item.change.valueVector3)
                    : item.change.property == "localScale"
                        ? Approximately(item.target.localScale, item.change.valueVector3)
                        : Approximately(item.target.localRotation, item.change.valueQuaternion);
                if (!valid) throw new InvalidOperationException("Final Transform validation failed: " + item.change.target.path);
            }
            foreach (var item in activeStates)
                if (item.target.gameObject.activeSelf != item.change.value)
                    throw new InvalidOperationException("Final Active State validation failed: " + item.change.target.path);
            foreach (var item in blendShapes)
                if (Mathf.Abs(item.renderer.GetBlendShapeWeight(item.index) - item.change.value) > Tolerance)
                    throw new InvalidOperationException("Final BlendShape validation failed: " + item.change.target.path + "/" + item.change.name);
        }

        private static string TransformKey(TransformChange change) => "transform|" + change.target.path + "|" + change.property;
        private static string ActiveKey(ActiveStateChange change) => "active|" + change.target.path;
        private static string BlendShapeKey(BlendShapeChange change) =>
            "blendShape|" + change.target.path + "|" + change.target.componentId + "|" + change.name;
        private static InvalidOperationException ChangedTarget(string path) =>
            new InvalidOperationException("Target changed after preview or is ambiguous: " + path + ". Build a new preview.");
        private static Vector3 ToUnity(Vector3Value value) => new Vector3(value.x, value.y, value.z);
        private static Quaternion ToUnity(QuaternionValue value) => new Quaternion(value.x, value.y, value.z, value.w);
        private static bool Approximately(Vector3 current, Vector3Value expected) =>
            Mathf.Abs(current.x - expected.x) <= Tolerance && Mathf.Abs(current.y - expected.y) <= Tolerance &&
            Mathf.Abs(current.z - expected.z) <= Tolerance;
        private static bool Approximately(Quaternion current, QuaternionValue expected) =>
            Mathf.Abs(Mathf.Abs(Quaternion.Dot(current.normalized, ToUnity(expected).normalized)) - 1f) <= Tolerance;
    }
}
