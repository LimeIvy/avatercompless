using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                AddMaterials(snapshot, transform, path);
                AddModularAvatarComponents(snapshot, avatarRoot, transform, path);
                transforms.Add(transform);

                for (var index = transform.childCount - 1; index >= 0; index--)
                    pending.Push(transform.GetChild(index));
            }

            AddPrefabInstances(snapshot, avatarRoot, transforms, paths);

            snapshot.transforms.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
            snapshot.activeStates.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
            snapshot.blendShapes.Sort(CompareBlendShapes);
            snapshot.materials.Sort((left, right) =>
            {
                var pathResult = StringComparer.Ordinal.Compare(left.target.path, right.target.path);
                if (pathResult != 0) return pathResult;
                var componentResult = StringComparer.Ordinal.Compare(left.target.componentId, right.target.componentId);
                return componentResult != 0 ? componentResult : left.materialIndex.CompareTo(right.materialIndex);
            });
            snapshot.modularAvatarComponents.Sort((left, right) => StringComparer.Ordinal.Compare(left.key, right.key));
            return snapshot;
        }

        private static void AddModularAvatarComponents(AvatarSnapshot snapshot, GameObject avatarRoot,
            Transform transform, string path)
        {
            var components = transform.GetComponents<Component>();
            var sameTypeIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var component in components)
            {
                if (component == null) continue;
                var type = component.GetType();
                if (!string.Equals(type.Namespace, "nadena.dev.modular_avatar.core", StringComparison.Ordinal) ||
                    !type.Name.StartsWith("ModularAvatar", StringComparison.Ordinal)) continue;

                var typeName = type.FullName ?? type.Name;
                sameTypeIndices.TryGetValue(typeName, out var typeIndex);
                sameTypeIndices[typeName] = typeIndex + 1;
                var entry = new ModularAvatarComponentSnapshot
                {
                    key = path + "|" + typeName + "|" + typeIndex.ToString(CultureInfo.InvariantCulture),
                    path = path,
                    hostParentPath = ParentPath(path),
                    hostSiblingIndex = transform.GetSiblingIndex(),
                    hostLocalPosition = Normalize(transform.localPosition),
                    hostLocalRotation = Normalize(transform.localRotation),
                    hostLocalScale = Normalize(transform.localScale),
                    hostActive = transform.gameObject.activeSelf,
                    componentType = typeName,
                    componentName = type.Name,
                    enabled = !(component is Behaviour behaviour) || behaviour.enabled
                };
                CaptureModularAvatarProperties(entry, avatarRoot, component);
                snapshot.modularAvatarComponents.Add(entry);
            }
        }

        private static string ParentPath(string path)
        {
            var separator = (path ?? string.Empty).LastIndexOf('/');
            return separator < 0 ? string.Empty : path.Substring(0, separator);
        }

        private static void CaptureModularAvatarProperties(ModularAvatarComponentSnapshot entry, GameObject avatarRoot,
            Component component)
        {
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.GetIterator();
            var enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = property.propertyType == UnityEditor.SerializedPropertyType.Generic;
                if (property.propertyType == UnityEditor.SerializedPropertyType.Generic ||
                    !IsUserFacingModularPropertyPath(property.propertyPath))
                    continue;

                var value = SerializedPropertyValue(property, avatarRoot);
                if (value == null) continue;
                entry.properties.Add(new ModularAvatarPropertySnapshot
                {
                    path = property.propertyPath,
                    value = value
                });
            }
            entry.properties.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
        }

        public static bool IsUserFacingModularPropertyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var rootPropertyName = path.Split('.')[0];
            if (rootPropertyName == "m_Script" || rootPropertyName == "m_ObjectHideFlags" ||
                rootPropertyName == "m_CorrespondingSourceObject" || rootPropertyName == "m_PrefabInstance" ||
                rootPropertyName == "m_PrefabAsset" || rootPropertyName == "m_GameObject" ||
                rootPropertyName == "m_Enabled" || rootPropertyName == "m_EditorHideFlags" ||
                rootPropertyName == "m_Name" || rootPropertyName == "m_EditorClassIdentifier" ||
                rootPropertyName == "_modularAvatarVersionTag") return false;
            if (path.EndsWith(".Array.size", StringComparison.Ordinal) ||
                path.EndsWith(".Object.targetObject", StringComparison.Ordinal) ||
                path.EndsWith(".m_FileID", StringComparison.Ordinal) ||
                path.EndsWith(".m_PathID", StringComparison.Ordinal)) return false;

            var arrayElement = path.LastIndexOf(".Array.data[", StringComparison.Ordinal);
            return arrayElement < 0 || path.IndexOf(']', arrayElement) != path.Length - 1;
        }

        private static string SerializedPropertyValue(SerializedProperty property, GameObject avatarRoot)
        {
            switch (property.propertyType)
            {
                case UnityEditor.SerializedPropertyType.Integer:
                case UnityEditor.SerializedPropertyType.ArraySize:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case UnityEditor.SerializedPropertyType.Boolean:
                    return property.boolValue ? "true" : "false";
                case UnityEditor.SerializedPropertyType.Float:
                    return Normalize(property.floatValue).ToString("0.#####", CultureInfo.InvariantCulture);
                case UnityEditor.SerializedPropertyType.String:
                    return property.stringValue ?? string.Empty;
                case UnityEditor.SerializedPropertyType.Enum:
                    return property.enumValueIndex.ToString(CultureInfo.InvariantCulture) + ":" + property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex);
                case UnityEditor.SerializedPropertyType.ObjectReference:
                    return ObjectReferenceValue(property.objectReferenceValue, avatarRoot);
                case UnityEditor.SerializedPropertyType.Vector2:
                    return Vector(property.vector2Value);
                case UnityEditor.SerializedPropertyType.Vector3:
                    return Vector(property.vector3Value);
                case UnityEditor.SerializedPropertyType.Vector4:
                    return Vector(property.vector4Value);
                case UnityEditor.SerializedPropertyType.Color:
                    var color = property.colorValue;
                    return string.Join(",", new[] { color.r, color.g, color.b, color.a }.Select(value => Normalize(value).ToString("0.#####", CultureInfo.InvariantCulture)));
                case UnityEditor.SerializedPropertyType.Quaternion:
                    var rotation = property.quaternionValue;
                    return string.Join(",", new[] { rotation.x, rotation.y, rotation.z, rotation.w }.Select(value => Normalize(value).ToString("0.#####", CultureInfo.InvariantCulture)));
                case UnityEditor.SerializedPropertyType.AnimationCurve:
                    return Curve(property.animationCurveValue);
                case UnityEditor.SerializedPropertyType.Character:
                    return ((int)property.intValue).ToString(CultureInfo.InvariantCulture);
                case UnityEditor.SerializedPropertyType.ManagedReference:
                    return property.managedReferenceFullTypename ?? string.Empty;
                default:
                    return property.propertyType + ":" + property.ToString();
            }
        }

        private static string ObjectReferenceValue(UnityEngine.Object value, GameObject avatarRoot)
        {
            if (value == null) return "null";
            if (value is GameObject gameObject && (gameObject == avatarRoot || gameObject.transform.IsChildOf(avatarRoot.transform)))
                return "avatar:" + GetRelativePath(avatarRoot.transform, gameObject.transform);
            if (value is Component component && (component.transform == avatarRoot.transform || component.transform.IsChildOf(avatarRoot.transform)))
                return "avatar:" + GetRelativePath(avatarRoot.transform, component.transform) + "#" + (component.GetType().FullName ?? component.GetType().Name);
            if (value is Transform transform && (transform == avatarRoot.transform || transform.IsChildOf(avatarRoot.transform)))
                return "avatar:" + GetRelativePath(avatarRoot.transform, transform);

            var assetPath = AssetDatabase.GetAssetPath(value);
            if (!string.IsNullOrEmpty(assetPath))
                return "asset:" + AssetDatabase.AssetPathToGUID(assetPath) + ":" + assetPath.Replace('\\', '/');
            return "external:" + (value.GetType().FullName ?? value.GetType().Name) + ":" + value.name;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var segments = new List<string>();
            for (var current = target; current != null && current != root; current = current.parent)
                segments.Add(Uri.EscapeDataString(current.name));
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string Vector(Vector2 value) => string.Join(",", new[] { value.x, value.y }.Select(item => Normalize(item).ToString("0.#####", CultureInfo.InvariantCulture)));
        private static string Vector(Vector3 value) => string.Join(",", new[] { value.x, value.y, value.z }.Select(item => Normalize(item).ToString("0.#####", CultureInfo.InvariantCulture)));
        private static string Vector(Vector4 value) => string.Join(",", new[] { value.x, value.y, value.z, value.w }.Select(item => Normalize(item).ToString("0.#####", CultureInfo.InvariantCulture)));

        private static string Curve(AnimationCurve curve)
        {
            return string.Join(";", curve.keys.Select(key => string.Join(",", new[] { key.time, key.value, key.inTangent, key.outTangent }
                .Select(item => Normalize(item).ToString("0.#####", CultureInfo.InvariantCulture)))));
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

        internal static Vector4Value Normalize(Vector4Value value) => new Vector4Value(
            Normalize(value.x), Normalize(value.y), Normalize(value.z), Normalize(value.w));

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

        private static void AddMaterials(AvatarSnapshot snapshot, Transform transform, string path)
        {
            var renderers = transform.GetComponents<Renderer>();
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    var item = new MaterialSlotSnapshot
                    {
                        target = new TargetLocator
                        {
                            scope = TargetLocator.BaseScope,
                            path = path,
                            componentId = "Renderer:" + rendererIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        },
                        materialIndex = materialIndex,
                        hasMaterial = material != null,
                        hasShader = material != null && material.shader != null,
                        material = GetAssetReference(material),
                        shader = material == null ? null : GetAssetReference(material.shader),
                        renderQueue = material == null ? -1 : material.renderQueue,
                        shaderKeywords = material == null ? Array.Empty<string>() : material.shaderKeywords.OrderBy(item => item, StringComparer.Ordinal).ToArray()
                    };
                    if (material != null && material.shader != null)
                        item.properties = CaptureMaterialProperties(material);
                    snapshot.materials.Add(item);
                }
            }
        }

        private static List<MaterialPropertySnapshot> CaptureMaterialProperties(Material material)
        {
            var result = new List<MaterialPropertySnapshot>();
            var shader = material.shader;
            var count = ShaderUtil.GetPropertyCount(shader);
            for (var index = 0; index < count; index++)
            {
                var name = ShaderUtil.GetPropertyName(shader, index);
                var property = new MaterialPropertySnapshot { name = name };
                switch (ShaderUtil.GetPropertyType(shader, index))
                {
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        property.type = "float";
                        property.floatValue = Normalize(material.GetFloat(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        var color = material.GetColor(name);
                        property.type = "color";
                        property.vectorValue = Normalize(new Vector4Value(color.r, color.g, color.b, color.a));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        var vector = material.GetVector(name);
                        property.type = "vector";
                        property.vectorValue = Normalize(new Vector4Value(vector.x, vector.y, vector.z, vector.w));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        property.type = "texture";
                        var texture = material.GetTexture(name);
                        property.hasTexture = texture != null;
                        property.texture = GetAssetReference(texture);
                        var scale = material.GetTextureScale(name);
                        var offset = material.GetTextureOffset(name);
                        property.textureScale = Normalize(new Vector4Value(scale.x, scale.y, 0f, 0f));
                        property.textureOffset = Normalize(new Vector4Value(offset.x, offset.y, 0f, 0f));
                        break;
                    default:
                        continue;
                }
                result.Add(property);
            }
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.name, right.name));
            return result;
        }

        private static AssetReference GetAssetReference(UnityEngine.Object asset)
        {
            if (asset == null) return null;
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return null;
            return new AssetReference
            {
                name = asset.name,
                guid = AssetDatabase.AssetPathToGUID(path),
                assetPath = path.Replace('\\', '/')
            };
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
