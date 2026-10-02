using System;
using System.Collections.Generic;
using AvatarRecipe.Editor.Core.Models;
using UnityEditor;
using UnityEngine;

namespace AvatarRecipe.Editor.Core.Snapshot
{
    public static class AvatarSnapshotBuilder
    {
        public const int FloatPrecision = 5;

        public static AvatarSnapshot Build(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                throw new ArgumentNullException(nameof(avatarRoot));
            }

            var snapshot = new AvatarSnapshot();
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            var paths = new Dictionary<Transform, string>();
            var transforms = new List<Transform>();
            var pending = new Stack<Transform>();
            pending.Push(avatarRoot.transform);

            while (pending.Count > 0)
            {
                var transform = pending.Pop();
                var path = transform == avatarRoot.transform
                    ? string.Empty
                    : JoinPath(paths[transform.parent], transform.name);
                paths.Add(transform, path);
                if (!seenPaths.Add(path))
                {
                    throw new InvalidOperationException(
                        "Avatar hierarchy contains ambiguous duplicate paths: " + DisplayPath(path));
                }

                snapshot.transforms.Add(new TransformSnapshot
                {
                    path = path,
                    localPosition = Normalize(transform.localPosition),
                    localRotation = Normalize(transform.localRotation),
                    localScale = Normalize(transform.localScale)
                });
                snapshot.activeStates.Add(new ActiveSnapshot
                {
                    path = path,
                    activeSelf = transform.gameObject.activeSelf
                });

                AddBlendShapes(snapshot, transform, path);
                transforms.Add(transform);

                for (var index = transform.childCount - 1; index >= 0; index--)
                    pending.Push(transform.GetChild(index));
            }

            AddPrefabInstances(snapshot, avatarRoot, transforms, paths);

            snapshot.transforms.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
            snapshot.activeStates.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
            snapshot.blendShapes.Sort(CompareBlendShapes);
            return snapshot;
        }

        private static void AddPrefabInstances(AvatarSnapshot snapshot, GameObject avatarRoot,
            IList<Transform> transforms, IReadOnlyDictionary<Transform, string> paths)
        {
            var rootHasPrefabContext = PrefabUtility.IsPartOfPrefabAsset(avatarRoot) ||
                                      PrefabUtility.IsPartOfPrefabInstance(avatarRoot);
            foreach (var transform in transforms)
            {
                if (transform == avatarRoot.transform || !PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject) ||
                    IsNestedPrefabRoot(transform, avatarRoot.transform) ||
                    (rootHasPrefabContext && !PrefabUtility.IsAddedGameObjectOverride(transform.gameObject)))
                {
                    continue;
                }

                var source = PrefabUtility.GetCorrespondingObjectFromSource<GameObject>(transform.gameObject);
                var assetPath = source == null ? string.Empty : AssetDatabase.GetAssetPath(source);
                var guid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
                var globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(transform.gameObject).ToString();
                if (source == null || string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(guid) ||
                    string.IsNullOrEmpty(globalObjectId) || globalObjectId.EndsWith("-0-0", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Added Prefab source could not be identified at " +
                                                        DisplayPath(paths[transform]) + ".");
                }

                snapshot.addedPrefabs.Add(new AddedPrefabSnapshot
                {
                    globalObjectId = globalObjectId,
                    path = paths[transform],
                    sourceName = source.name,
                    sourceGuid = guid,
                    sourceAssetPath = assetPath.Replace('\\', '/'),
                    siblingIndex = transform.GetSiblingIndex(),
                    localPosition = Normalize(transform.localPosition),
                    localRotation = Normalize(transform.localRotation),
                    localScale = Normalize(transform.localScale)
                });
            }

            snapshot.addedPrefabs.Sort((left, right) => StringComparer.Ordinal.Compare(left.globalObjectId, right.globalObjectId));
        }

        private static bool IsNestedPrefabRoot(Transform candidate, Transform avatarRoot)
        {
            for (var parent = candidate.parent; parent != null && parent != avatarRoot; parent = parent.parent)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(parent.gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        internal static float Normalize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException("Avatar snapshot contains a non-finite number.");
            }

            var rounded = (float)Math.Round(value, FloatPrecision, MidpointRounding.AwayFromZero);
            return rounded == 0f ? 0f : rounded;
        }

        internal static Vector3Value Normalize(Vector3 value)
        {
            return new Vector3Value(Normalize(value.x), Normalize(value.y), Normalize(value.z));
        }

        internal static QuaternionValue Normalize(Quaternion value)
        {
            var magnitudeSquared = (double)value.x * value.x + (double)value.y * value.y +
                                   (double)value.z * value.z + (double)value.w * value.w;
            if (double.IsNaN(magnitudeSquared) || double.IsInfinity(magnitudeSquared) || magnitudeSquared < 1e-12)
            {
                throw new InvalidOperationException("Avatar snapshot contains an invalid local rotation.");
            }

            var inverseMagnitude = 1.0 / Math.Sqrt(magnitudeSquared);
            var x = value.x * inverseMagnitude;
            var y = value.y * inverseMagnitude;
            var z = value.z * inverseMagnitude;
            var w = value.w * inverseMagnitude;

            // q and -q represent the same rotation. Choose one sign for stable output.
            if (w < 0 || (w == 0 && (x < 0 || (x == 0 && (y < 0 || (y == 0 && z < 0))))))
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            return new QuaternionValue(Normalize((float)x), Normalize((float)y), Normalize((float)z), Normalize((float)w));
        }

        private static void AddBlendShapes(AvatarSnapshot snapshot, Transform transform, string path)
        {
            var renderers = transform.GetComponents<SkinnedMeshRenderer>();
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var seenNames = new HashSet<string>(StringComparer.Ordinal);
                var componentId = "SkinnedMeshRenderer:" + rendererIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                for (var shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    var name = mesh.GetBlendShapeName(shapeIndex);
                    if (!seenNames.Add(name))
                    {
                        throw new InvalidOperationException(
                            "SkinnedMeshRenderer contains ambiguous duplicate BlendShape names at " + DisplayPath(path) +
                            ": " + name);
                    }

                    snapshot.blendShapes.Add(new BlendShapeSnapshot
                    {
                        target = new TargetLocator
                        {
                            scope = TargetLocator.BaseScope,
                            path = path,
                            componentId = componentId
                        },
                        name = name,
                        weight = Normalize(renderer.GetBlendShapeWeight(shapeIndex))
                    });
                }
            }
        }

        private static int CompareBlendShapes(BlendShapeSnapshot left, BlendShapeSnapshot right)
        {
            var result = StringComparer.Ordinal.Compare(left.target.path, right.target.path);
            if (result != 0)
            {
                return result;
            }

            result = StringComparer.Ordinal.Compare(left.target.componentId, right.target.componentId);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left.name, right.name);
        }

        private static string JoinPath(string parentPath, string name)
        {
            var escapedName = Uri.EscapeDataString(name);
            return string.IsNullOrEmpty(parentPath) ? escapedName : parentPath + "/" + escapedName;
        }

        private static string DisplayPath(string path)
        {
            return string.IsNullOrEmpty(path) ? "<Avatar Root>" : path;
        }
    }
}
