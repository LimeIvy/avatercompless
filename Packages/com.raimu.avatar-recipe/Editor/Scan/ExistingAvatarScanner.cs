using System;
using AvatarRecipe.Editor.Core.Diff;
using AvatarRecipe.Editor.Core.Models;
using AvatarRecipe.Editor.Core.Snapshot;
using AvatarRecipe.Editor.Tracking;
using UnityEditor;
using UnityEngine;

namespace AvatarRecipe.Editor.Scan
{
    public static class ExistingAvatarScanner
    {
        public static RecipeState Compare(GameObject modifiedAvatar, GameObject originalAvatarPrefab)
        {
            ValidateModifiedAvatar(modifiedAvatar);
            GetBaseAvatarReference(originalAvatarPrefab);
            return AvatarDiffEngine.Diff(
                BuildOriginalSnapshot(originalAvatarPrefab),
                AvatarSnapshotBuilder.Build(modifiedAvatar));
        }

        internal static RecipeState ScanAndStartTracking(GameObject modifiedAvatar, GameObject originalAvatarPrefab)
        {
            ValidateModifiedAvatar(modifiedAvatar);
            var baseAvatar = GetBaseAvatarReference(originalAvatarPrefab);

            var baseline = BuildOriginalSnapshot(originalAvatarPrefab);
            var current = AvatarSnapshotBuilder.Build(modifiedAvatar);
            var state = AvatarDiffEngine.Diff(baseline, current);

            // Scan seeds the normal tracking cache with the original snapshot. Recipe files
            // remain governed by the regular Unity scene-save callback.
            AvatarTrackingService.StartTrackingFromScan(modifiedAvatar, baseline, baseAvatar);
            return state;
        }

        private static void ValidateModifiedAvatar(GameObject modifiedAvatar)
        {
            if (modifiedAvatar == null)
            {
                throw new ArgumentNullException(nameof(modifiedAvatar));
            }

            var scene = modifiedAvatar.scene;
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("Modified Avatar must belong to a saved, loaded scene. Save the scene first.");
            }
        }

        private static BaseAvatarReference GetBaseAvatarReference(GameObject originalAvatarPrefab)
        {
            if (originalAvatarPrefab == null)
            {
                throw new ArgumentNullException(nameof(originalAvatarPrefab));
            }

            var path = AssetDatabase.GetAssetPath(originalAvatarPrefab);
            if (!PrefabUtility.IsPartOfPrefabAsset(originalAvatarPrefab) || string.IsNullOrEmpty(path) ||
                AssetDatabase.LoadMainAssetAtPath(path) != originalAvatarPrefab)
            {
                throw new InvalidOperationException("Original Avatar must be the root Prefab asset, not a scene object or nested Prefab object.");
            }

            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException("Unity could not identify the Original Avatar Prefab asset.");
            }

            return new BaseAvatarReference
            {
                name = originalAvatarPrefab.name,
                prefabGuid = guid,
                assetPath = path.Replace('\\', '/')
            };
        }

        private static AvatarSnapshot BuildOriginalSnapshot(GameObject originalAvatarPrefab)
        {
            var assetPath = AssetDatabase.GetAssetPath(originalAvatarPrefab);
            GameObject loadedPrefab = null;
            try
            {
                loadedPrefab = PrefabUtility.LoadPrefabContents(assetPath);
                if (loadedPrefab == null)
                {
                    throw new InvalidOperationException("Unity could not load the Original Avatar Prefab contents: " + assetPath);
                }
                return AvatarSnapshotBuilder.Build(loadedPrefab);
            }
            finally
            {
                if (loadedPrefab != null) PrefabUtility.UnloadPrefabContents(loadedPrefab);
            }
        }
    }
}
