using System;
using System.Collections.Generic;
using System.Linq;
using AvatarRecipe.Editor.Core.Models;
using AvatarRecipe.Editor.Localization;
using UnityEditor;
using UnityEngine;

namespace AvatarRecipe.Editor.Apply
{
    internal static class ApplyPlanner
    {
        private const float Tolerance = 0.00001f;

        public static ApplyPlan Build(LoadedRecipe recipe, GameObject target)
        {
            if (recipe == null || recipe.state == null) throw new ArgumentNullException(nameof(recipe));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var plan = new ApplyPlan();
            CheckBaseAvatar(recipe.baseAvatar, target, plan);
            foreach (var prefab in recipe.state.addedPrefabs)
            {
                if ((!string.IsNullOrEmpty(recipe.baseAvatar.prefabGuid) && prefab.guid == recipe.baseAvatar.prefabGuid) ||
                    (!string.IsNullOrEmpty(recipe.baseAvatar.assetPath) &&
                     string.Equals(prefab.assetPath.Replace('\\', '/'), recipe.baseAvatar.assetPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                {
                    plan.compatibility.Add(new CompatibilityItem
                    {
                        status = CompatibilityStatus.ManualReview,
                        category = "Required Prefab",
                        message = AvatarRecipeLocalization.Format("{0} resolves to the Base Avatar Prefab and cannot be treated as an independently added Prefab.", prefab.name),
                        targetPath = prefab.parentPath
                    });
                    continue;
                }

                var asset = ResolvePrefab(prefab, out var status, out var resolution);
                plan.compatibility.Add(new CompatibilityItem
                {
                    status = status,
                    category = "Required Prefab",
                    message = resolution,
                    targetPath = prefab.parentPath
                });
                if (asset == null) continue;
                var parent = ResolvePath(target.transform, prefab.parentPath, out var ambiguous);
                if (parent == null)
                {
                    plan.compatibility.Add(new CompatibilityItem
                    {
                        status = ambiguous ? CompatibilityStatus.Ambiguous : CompatibilityStatus.Missing,
                        category = "Prefab Parent",
                        message = AvatarRecipeLocalization.Format("Could not resolve parent for {0} at {1}", prefab.name, DisplayPath(prefab.parentPath)),
                        targetPath = prefab.parentPath
                    });
                    continue;
                }
                plan.operations.Add(new PlannedOperation
                {
                    kind = "Required Prefab",
                    targetPath = DisplayPath(prefab.parentPath),
                    description = AvatarRecipeLocalization.Format("{0} (sibling {1})", prefab.name, prefab.siblingIndex)
                });
            }

            foreach (var change in recipe.state.transformChanges)
                PlanTransform(target.transform, change, plan);
            foreach (var change in recipe.state.activeStateChanges)
                PlanActive(target.transform, change, plan);
            foreach (var change in recipe.state.blendShapeChanges)
                PlanBlendShape(target.transform, change, plan);
            foreach (var warning in recipe.state.manualReview)
                plan.compatibility.Add(new CompatibilityItem
                {
                    status = CompatibilityStatus.ManualReview,
                    category = "Manual Review",
                    message = warning,
                    targetPath = string.Empty
                });
            return plan;
        }

        private static void CheckBaseAvatar(BaseAvatarMetadata expected, GameObject target, ApplyPlan plan)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource<GameObject>(target);
            var targetPath = source == null ? string.Empty : AssetDatabase.GetAssetPath(source);
            var targetGuid = string.IsNullOrEmpty(targetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(targetPath);
            var exact = !string.IsNullOrEmpty(expected.prefabGuid) && expected.prefabGuid == targetGuid;
            if (!exact && !string.IsNullOrEmpty(expected.assetPath))
                exact = string.Equals(expected.assetPath.Replace('\\', '/'), targetPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

            var status = exact ? CompatibilityStatus.Found :
                source == null ? CompatibilityStatus.Missing : CompatibilityStatus.Conflict;
            var detail = exact ? AvatarRecipeLocalization.Format("Base Avatar matches {0}", expected.name) : source == null
                ? AvatarRecipeLocalization.Format("Target Avatar is not connected to a source Prefab; expected {0}", expected.name)
                : AvatarRecipeLocalization.Format("Target source Prefab {0} does not match expected {1} ({2})", targetPath, expected.assetPath, expected.name);
            plan.compatibility.Add(new CompatibilityItem
            {
                status = status,
                category = "Base Avatar",
                message = detail,
                targetPath = string.Empty,
                changeKey = "base"
            });
        }

        internal static GameObject ResolvePrefab(AddedPrefabEntry entry, out CompatibilityStatus status, out string message)
        {
            if (!string.IsNullOrEmpty(entry.guid))
            {
                var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                var asset = LoadPrefab(path);
                if (asset != null)
                {
                    status = CompatibilityStatus.Found;
                    message = AvatarRecipeLocalization.Format("{0}: resolved by GUID at {1}", entry.name, path);
                    return asset;
                }
            }

            var pathAsset = LoadPrefab(entry.assetPath);
            if (pathAsset != null)
            {
                status = CompatibilityStatus.Found;
                message = AvatarRecipeLocalization.Format("{0}: resolved by asset path {1}", entry.name, entry.assetPath);
                return pathAsset;
            }

            var candidates = AssetDatabase.FindAssets("t:Prefab " + entry.name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(LoadPrefab)
                .Where(asset => asset != null && string.Equals(asset.name, entry.name, StringComparison.Ordinal))
                .ToList();
            if (candidates.Count == 1)
            {
                status = CompatibilityStatus.Found;
                message = AvatarRecipeLocalization.Format("{0}: resolved by exact name at {1}", entry.name, AssetDatabase.GetAssetPath(candidates[0]));
                return candidates[0];
            }
            if (candidates.Count > 1)
            {
                status = CompatibilityStatus.Ambiguous;
                message = AvatarRecipeLocalization.Format("multiple exact-name candidates: {0}", string.Join(", ", candidates.Select(AssetDatabase.GetAssetPath).OrderBy(path => path, StringComparer.Ordinal)));
                return null;
            }
            status = CompatibilityStatus.Missing;
            message = AvatarRecipeLocalization.Format("missing; expected {0} (GUID {1})", entry.assetPath, entry.guid);
            return null;
        }

        private static GameObject LoadPrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return asset != null && PrefabUtility.IsPartOfPrefabAsset(asset) ? asset : null;
        }

        private static void PlanTransform(Transform root, TransformChange change, ApplyPlan plan)
        {
            var target = ResolvePath(root, change.target.path, out var ambiguous);
            if (target == null)
            {
                AddTargetIssue(plan, "Transform", change.target.path, ambiguous);
                return;
            }
            var currentVector = change.property == "localPosition" ? target.localPosition : target.localScale;
            var conflict = change.property == "localRotation"
                ? !Approximately(target.localRotation, change.baselineQuaternion) && !Approximately(target.localRotation, change.valueQuaternion)
                : !Approximately(currentVector, change.baselineVector3) && !Approximately(currentVector, change.valueVector3);
            AddOperation(plan, "Transform", change.target.path, AvatarRecipeLocalization.Format("{0} — Recipe value", change.property), conflict,
                "transform|" + change.target.path + "|" + change.property);
        }

        private static void PlanActive(Transform root, ActiveStateChange change, ApplyPlan plan)
        {
            var target = ResolvePath(root, change.target.path, out var ambiguous);
            if (target == null)
            {
                AddTargetIssue(plan, "Active State", change.target.path, ambiguous);
                return;
            }
            AddOperation(plan, "Active State", change.target.path,
                AvatarRecipeLocalization.Format("{0} → {1}", change.baseline, change.value),
                target.gameObject.activeSelf != change.baseline && target.gameObject.activeSelf != change.value,
                "active|" + change.target.path);
        }

        private static void PlanBlendShape(Transform root, BlendShapeChange change, ApplyPlan plan)
        {
            var target = ResolvePath(root, change.target.path, out var ambiguous);
            if (target == null)
            {
                AddTargetIssue(plan, "BlendShape", change.target.path, ambiguous);
                return;
            }
            var renderers = target.GetComponents<SkinnedMeshRenderer>();
            if (!TryGetRendererIndex(change.target.componentId, out var index) || index >= renderers.Length ||
                renderers[index] == null || renderers[index].sharedMesh == null)
            {
                AddTargetIssue(plan, "BlendShape Renderer", change.target.path + "/" + change.target.componentId, false);
                return;
            }
            var renderer = renderers[index];
            var shapeIndex = -1;
            for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                if (string.Equals(renderer.sharedMesh.GetBlendShapeName(i), change.name, StringComparison.Ordinal))
                {
                    if (shapeIndex >= 0)
                    {
                        AddTargetIssue(plan, "BlendShape", change.target.path + "/" + change.name, true);
                        return;
                    }
                    shapeIndex = i;
                }
            if (shapeIndex < 0)
            {
                AddTargetIssue(plan, "BlendShape", change.target.path + "/" + change.name, false);
                return;
            }
            var current = renderer.GetBlendShapeWeight(shapeIndex);
            AddOperation(plan, "BlendShape", change.target.path,
                AvatarRecipeLocalization.Format("{0} → {1}", change.name + ": " + current, change.value),
                !Approximately(current, change.baseline) && !Approximately(current, change.value),
                "blendShape|" + change.target.path + "|" + change.target.componentId + "|" + change.name);
        }

        private static void AddOperation(ApplyPlan plan, string kind, string path, string description, bool conflict,
            string changeKey)
        {
            plan.compatibility.Add(new CompatibilityItem
            {
                status = conflict ? CompatibilityStatus.Conflict : CompatibilityStatus.Found,
                category = kind,
                message = conflict ? AvatarRecipeLocalization.Format("Current value differs from Recipe baseline at {0}", DisplayPath(path)) : AvatarRecipeLocalization.Format("Target found at {0}", DisplayPath(path)),
                targetPath = path,
                changeKey = changeKey
            });
            plan.operations.Add(new PlannedOperation { kind = kind, targetPath = DisplayPath(path), description = description });
        }

        private static void AddTargetIssue(ApplyPlan plan, string kind, string path, bool ambiguous)
        {
            plan.compatibility.Add(new CompatibilityItem
            {
                status = ambiguous ? CompatibilityStatus.Ambiguous : CompatibilityStatus.Missing,
                category = kind,
                message = AvatarRecipeLocalization.Format(ambiguous ? "Ambiguous target: {0}" : "Missing target: {0}", DisplayPath(path)),
                targetPath = path
            });
        }

        internal static Transform ResolvePath(Transform root, string path, out bool ambiguous)
        {
            ambiguous = false;
            var current = root;
            if (string.IsNullOrEmpty(path)) return current;
            foreach (var segment in path.Split('/'))
            {
                var name = Uri.UnescapeDataString(segment);
                Transform match = null;
                for (var i = 0; i < current.childCount; i++)
                {
                    var child = current.GetChild(i);
                    if (!string.Equals(child.name, name, StringComparison.Ordinal)) continue;
                    if (match != null)
                    {
                        ambiguous = true;
                        return null;
                    }
                    match = child;
                }
                if (match == null) return null;
                current = match;
            }
            return current;
        }

        private static bool TryGetRendererIndex(string componentId, out int index)
        {
            const string prefix = "SkinnedMeshRenderer:";
            if (componentId != null && componentId.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(componentId.Substring(prefix.Length), out index)) return true;
            index = -1;
            return false;
        }

        private static bool Approximately(Vector3 current, Vector3Value expected) =>
            Mathf.Abs(current.x - expected.x) <= Tolerance && Mathf.Abs(current.y - expected.y) <= Tolerance && Mathf.Abs(current.z - expected.z) <= Tolerance;
        private static bool Approximately(Quaternion current, QuaternionValue expected) =>
            Mathf.Abs(Mathf.Abs(Quaternion.Dot(current.normalized, new Quaternion(expected.x, expected.y, expected.z, expected.w).normalized)) - 1f) <= Tolerance;
        private static bool Approximately(float current, float expected) => Mathf.Abs(current - expected) <= Tolerance;
        private static string DisplayPath(string path) => string.IsNullOrEmpty(path) ? "<Avatar Root>" : path;
    }
}
