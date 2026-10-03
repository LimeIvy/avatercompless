using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        private sealed class PreparedMaterial
        {
            public PlannedOperation operation;
            public MaterialChange change;
            public Renderer renderer;
            public Material valueMaterial;
            public Shader valueShader;
            public string variantPath;
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
            var materials = new List<PreparedMaterial>();
            var modularOperations = new List<PlannedOperation>();

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
                foreach (var operation in plan.operations.Where(item => item.materialChange != null))
                {
                    if (skipConflicts && conflicts.Contains(operation.changeKey)) { result.skippedConflicts++; continue; }
                    if (operation.renderer == null || operation.materialChange.materialIndex < 0 ||
                        operation.materialChange.materialIndex >= operation.renderer.sharedMaterials.Length)
                        throw ChangedTarget(operation.targetPath);
                    materials.Add(new PreparedMaterial
                    {
                        operation = operation,
                        change = operation.materialChange,
                        renderer = operation.renderer,
                        valueMaterial = operation.valueMaterial,
                        valueShader = operation.valueShader,
                        variantPath = operation.variantPath
                    });
                }
                foreach (var operation in plan.operations.Where(item => item.modularAvatarChanges != null))
                {
                    if (skipConflicts && conflicts.Contains(operation.changeKey)) { result.skippedConflicts++; continue; }
                    var representative = operation.modularAvatarChanges.FirstOrDefault();
                    if (representative == null || !representative.hasHostPlacement) throw ChangedTarget(operation.targetPath);
                    var parent = ApplyPlanner.ResolvePath(target.transform, representative.hostParentPath, out var ambiguous);
                    var resolvedHost = ApplyPlanner.ResolvePath(target.transform, representative.path, out var hostAmbiguous);
                    if (parent == null || ambiguous || parent != operation.modularHostParent || hostAmbiguous ||
                        (representative.createHost && resolvedHost != null) ||
                        (!representative.createHost && (resolvedHost == null || resolvedHost != operation.modularHostObject)))
                        throw ChangedTarget(operation.targetPath);
                    foreach (var change in operation.modularAvatarChanges)
                    {
                        if (ResolveModularType(change.componentType) == null) throw ChangedTarget(change.componentType);
                        if (change.componentName == "ModularAvatarObjectToggle")
                            foreach (var toggleTarget in change.targets ?? new List<ModularAvatarTarget>())
                                if (ApplyPlanner.ResolvePath(target.transform, toggleTarget.path, out ambiguous) == null || ambiguous)
                                    throw ChangedTarget(toggleTarget.path);
                    }
                    modularOperations.Add(operation);
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

                foreach (var item in materials)
                {
                    var material = item.valueMaterial;
                    if (!string.IsNullOrEmpty(item.variantPath))
                        material = RequiresImportedMaterialCopy(item.change)
                            ? CreateOrLoadImportedMaterial(item)
                            : CreateOrLoadMaterialVariant(item);
                    var slots = item.renderer.sharedMaterials;
                    Undo.RecordObject(item.renderer, "Apply Avatar Recipe");
                    slots[item.change.materialIndex] = material;
                    item.renderer.sharedMaterials = slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.renderer);
                }

                foreach (var operation in modularOperations)
                    ApplyModularAvatarMenu(operation, target.transform, target.scene);

                ValidateResults(prefabs, createdPrefabs, transforms, activeStates, blendShapes, materials);
                EditorSceneManager.MarkSceneDirty(target.scene);
                Undo.CollapseUndoOperations(undoGroup);
                result.appliedOperations = prefabs.Count + transforms.Count + activeStates.Count + blendShapes.Count + materials.Count + modularOperations.Count;
                return result;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw new InvalidOperationException("Apply failed. The scene changes were reverted: " + exception.Message, exception);
            }
        }

        private static void ApplyModularAvatarMenu(PlannedOperation operation, Transform avatar, UnityEngine.SceneManagement.Scene scene)
        {
            var changes = operation.modularAvatarChanges;
            var hostChange = changes[0];
            GameObject host;
            if (hostChange.createHost)
            {
                host = new GameObject(Leaf(hostChange.path));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
                Undo.RegisterCreatedObjectUndo(host, "Apply Avatar Recipe");
                Undo.SetTransformParent(host.transform, operation.modularHostParent, false, "Apply Avatar Recipe");
                host.transform.localPosition = ToUnity(hostChange.hostLocalPosition);
                host.transform.localRotation = ToUnity(hostChange.hostLocalRotation);
                host.transform.localScale = ToUnity(hostChange.hostLocalScale);
                host.transform.SetSiblingIndex(Mathf.Clamp(hostChange.hostSiblingIndex, 0, operation.modularHostParent.childCount - 1));
                host.SetActive(hostChange.hostActive);
            }
            else
            {
                host = operation.modularHostObject.gameObject;
            }

            foreach (var change in changes.OrderBy(item => ComponentOrder(item.componentName)))
            {
                var type = ResolveModularType(change.componentType);
                if (type == null) throw new InvalidOperationException("Modular Avatar component is unavailable: " + change.componentType);
                var component = Undo.AddComponent(host, type);
                if (component == null) throw new InvalidOperationException("Could not add Modular Avatar component: " + change.componentType);
                Undo.RecordObject(component, "Apply Avatar Recipe");
                ApplyModularProperties(component, change, avatar);
                if (component is Behaviour behaviour) behaviour.enabled = change.valueEnabled;
            }
            ValidateModularAvatarMenu(operation, host, avatar);
        }

        private static void ValidateModularAvatarMenu(PlannedOperation operation, GameObject host, Transform avatar)
        {
            var change = operation.modularAvatarChanges[0];
            if (host == null || host.name != Leaf(change.path) || (change.createHost &&
                (host.transform.parent != operation.modularHostParent ||
                 !Approximately(host.transform.localPosition, change.hostLocalPosition) ||
                 !Approximately(host.transform.localRotation, change.hostLocalRotation) ||
                 !Approximately(host.transform.localScale, change.hostLocalScale) || host.activeSelf != change.hostActive)))
                throw new InvalidOperationException("Final Modular Avatar menu host validation failed: " + change.path);

            foreach (var item in operation.modularAvatarChanges)
            {
                var type = ResolveModularType(item.componentType);
                var component = type == null ? null : host.GetComponent(type);
                if (component == null || (component is Behaviour behaviour && behaviour.enabled != item.valueEnabled))
                    throw new InvalidOperationException("Final Modular Avatar component validation failed: " + item.componentType);
                if (item.componentName != "ModularAvatarObjectToggle") continue;
                var serialized = new SerializedObject(component);
                var objects = serialized.FindProperty("m_objects");
                if (objects == null || objects.arraySize != (item.targets ?? new List<ModularAvatarTarget>()).Count)
                    throw new InvalidOperationException("Final Modular Avatar toggle list validation failed: " + item.path);
                for (var index = 0; index < objects.arraySize; index++)
                {
                    var expected = ApplyPlanner.ResolvePath(avatar, item.targets[index].path, out var ambiguous);
                    var targetObject = objects.GetArrayElementAtIndex(index).FindPropertyRelative("Object.targetObject");
                    if (expected == null || ambiguous || targetObject == null || targetObject.objectReferenceValue != expected.gameObject)
                        throw new InvalidOperationException("Final Modular Avatar toggle target validation failed: " + item.targets[index].path);
                }
            }
        }

        private static int ComponentOrder(string name) => name == "ModularAvatarMenuInstaller" ? 0 :
            name == "ModularAvatarMenuItem" ? 1 : name == "ModularAvatarObjectToggle" ? 2 : 3;

        private static void ApplyModularProperties(Component component, ModularAvatarChange change, Transform avatar)
        {
            var serialized = new SerializedObject(component);
            if (change.componentName == "ModularAvatarObjectToggle")
            {
                var objectList = serialized.FindProperty("m_objects");
                if (objectList == null || !objectList.isArray) throw new InvalidOperationException("Modular Avatar Object Toggle list is unavailable.");
                objectList.arraySize = (change.targets ?? new List<ModularAvatarTarget>()).Count;
            }
            foreach (var item in change.properties ?? new List<ModularAvatarPropertyChange>())
            {
                if (!item.valueExists) throw new InvalidOperationException("Removing Modular Avatar properties is unsupported: " + item.path);
                var property = serialized.FindProperty(item.path);
                if (property == null) throw new InvalidOperationException("Modular Avatar property is unavailable: " + item.path);
                switch (property.propertyType)
                {
                    case SerializedPropertyType.String:
                        property.stringValue = item.value ?? string.Empty;
                        break;
                    case SerializedPropertyType.Boolean:
                        if (!bool.TryParse(item.value, out var boolean)) throw new InvalidOperationException("Invalid Modular Avatar boolean: " + item.path);
                        property.boolValue = boolean;
                        break;
                    case SerializedPropertyType.Enum:
                        var colon = (item.value ?? string.Empty).IndexOf(':');
                        var enumText = colon < 0 ? item.value : item.value.Substring(0, colon);
                        if (!int.TryParse(enumText, out var enumIndex) || enumIndex < 0 || enumIndex >= property.enumNames.Length)
                            throw new InvalidOperationException("Invalid Modular Avatar enum: " + item.path);
                        property.enumValueIndex = enumIndex;
                        break;
                    case SerializedPropertyType.Integer:
                        if (!int.TryParse(item.value, out var integer)) throw new InvalidOperationException("Invalid Modular Avatar integer: " + item.path);
                        property.intValue = integer;
                        break;
                    case SerializedPropertyType.Float:
                        if (!float.TryParse(item.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                            float.IsNaN(number) || float.IsInfinity(number))
                            throw new InvalidOperationException("Invalid Modular Avatar number: " + item.path);
                        property.floatValue = number;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        if (item.value != "null") throw new InvalidOperationException("Recipe contains a non-null Modular Avatar asset reference: " + item.path);
                        property.objectReferenceValue = null;
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported Modular Avatar property type at " + item.path + ": " + property.propertyType);
                }
            }

            if (change.componentName == "ModularAvatarObjectToggle")
            {
                var objects = serialized.FindProperty("m_objects");
                if (objects == null || !objects.isArray) throw new InvalidOperationException("Modular Avatar Object Toggle list is unavailable.");
                var targets = change.targets ?? new List<ModularAvatarTarget>();
                for (var index = 0; index < targets.Count; index++)
                {
                    var target = ApplyPlanner.ResolvePath(avatar, targets[index].path, out var ambiguous);
                    if (target == null || ambiguous) throw ChangedTarget(targets[index].path);
                    var element = objects.GetArrayElementAtIndex(index);
                    var targetObject = element.FindPropertyRelative("Object.targetObject");
                    if (targetObject != null) targetObject.objectReferenceValue = target.gameObject;
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        private static Type ResolveModularType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null && typeof(Component).IsAssignableFrom(type)) return type;
            }
            return null;
        }

        private static string Leaf(string path)
        {
            var separator = (path ?? string.Empty).LastIndexOf('/');
            return Uri.UnescapeDataString(separator < 0 ? path ?? string.Empty : path.Substring(separator + 1));
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

        private static Material CreateOrLoadMaterialVariant(PreparedMaterial item)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(item.variantPath);
            if (existing != null)
            {
                if (!MatchesVariant(existing, item.change))
                    throw new InvalidOperationException("Generated material asset already exists with different contents: " + item.variantPath);
                return existing;
            }
            if (AssetDatabase.LoadMainAssetAtPath(item.variantPath) != null)
                throw new InvalidOperationException("Generated material path is already occupied: " + item.variantPath);

            // Copy the Recipe's final source Material so untouched settings (including later LilToon layers)
            // do not leak in from a different target Avatar's Material.
            var source = item.valueMaterial;
            if (source == null && item.change.valueMaterial != null)
                source = LoadMaterial(item.change.valueMaterial.guid, item.change.valueMaterial.assetPath);
            if (source == null) source = item.renderer.sharedMaterials[item.change.materialIndex];
            if (source == null) source = item.change.baselineMaterial == null ? null :
                LoadMaterial(item.change.baselineMaterial.guid, item.change.baselineMaterial.assetPath);
            if (source == null) throw new InvalidOperationException("Could not load the baseline Material for a Recipe variant.");

            var folder = "Assets/AvatarRecipeGenerated";
            EnsureAssetFolder(folder);
            EnsureAssetFolder(folder + "/Materials");
            var clone = new Material(source) { name = Path.GetFileNameWithoutExtension(item.variantPath) };
            if (item.valueShader != null) clone.shader = item.valueShader;
            foreach (var property in item.change.properties ?? new List<MaterialPropertyChange>())
                if (property.valueExists && property.value != null) ApplyMaterialProperty(clone, property.name, property.value);
            clone.renderQueue = item.change.valueRenderQueue;
            clone.shaderKeywords = item.change.valueShaderKeywords ?? Array.Empty<string>();

            AssetDatabase.CreateAsset(clone, item.variantPath);
            AssetDatabase.ImportAsset(item.variantPath);
            return AssetDatabase.LoadAssetAtPath<Material>(item.variantPath);
        }

        private static Material CreateOrLoadImportedMaterial(PreparedMaterial item)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(item.variantPath);
            if (existing != null)
            {
                if (!MatchesImportedMaterial(existing, item.change))
                    throw new InvalidOperationException("Generated Material already exists with different contents: " + item.variantPath);
                return existing;
            }
            if (AssetDatabase.LoadMainAssetAtPath(item.variantPath) != null)
                throw new InvalidOperationException("Generated Material path is already occupied: " + item.variantPath);
            if (item.valueShader == null)
                throw new InvalidOperationException("Could not resolve the Shader for the new Material.");

            var clone = new Material(item.valueShader)
            {
                name = item.change.valueMaterial == null ? Path.GetFileNameWithoutExtension(item.variantPath) : item.change.valueMaterial.name
            };
            foreach (var property in item.change.valueMaterialState ?? new List<MaterialPropertySnapshot>())
                ApplyMaterialProperty(clone, property.name, property);
            clone.renderQueue = item.change.valueRenderQueue;
            clone.shaderKeywords = item.change.valueShaderKeywords ?? Array.Empty<string>();

            var folder = Path.GetDirectoryName(item.variantPath).Replace('\\', '/');
            EnsureAssetFolder(folder);
            AssetDatabase.CreateAsset(clone, item.variantPath);
            AssetDatabase.ImportAsset(item.variantPath);
            var result = AssetDatabase.LoadAssetAtPath<Material>(item.variantPath);
            if (result == null || !MatchesImportedMaterial(result, item.change))
                throw new InvalidOperationException("Generated Material failed validation: " + item.variantPath);
            return result;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var separator = path.LastIndexOf('/');
            var parent = path.Substring(0, separator);
            var name = path.Substring(separator + 1);
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void ApplyMaterialProperty(Material material, string name, MaterialPropertySnapshot value)
        {
            if (!material.HasProperty(name))
                throw new InvalidOperationException("The selected Shader does not contain Material property " + name + ".");
            switch (value.type)
            {
                case "float": material.SetFloat(name, value.floatValue); break;
                case "color":
                    material.SetColor(name, new Color(value.vectorValue.x, value.vectorValue.y, value.vectorValue.z, value.vectorValue.w));
                    break;
                case "vector":
                    material.SetVector(name, new Vector4(value.vectorValue.x, value.vectorValue.y, value.vectorValue.z, value.vectorValue.w));
                    break;
                case "texture":
                    var texture = !value.hasTexture || value.texture == null ? null : AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(value.texture.guid));
                    if (texture == null && value.texture != null) texture = AssetDatabase.LoadAssetAtPath<Texture>(value.texture.assetPath);
                    if (value.hasTexture && texture == null)
                        throw new InvalidOperationException("Could not resolve Material texture: " + (value.texture == null ? name : value.texture.assetPath));
                    material.SetTexture(name, texture);
                    material.SetTextureScale(name, new Vector2(value.textureScale.x, value.textureScale.y));
                    material.SetTextureOffset(name, new Vector2(value.textureOffset.x, value.textureOffset.y));
                    break;
                default: throw new InvalidOperationException("Unsupported Material property type: " + value.type);
            }
        }

        private static Material LoadMaterial(string guid, string path)
        {
            var material = string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            return material != null || string.IsNullOrEmpty(path) ? material : AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static bool MatchesVariant(Material material, MaterialChange change)
        {
            if (material.renderQueue != change.valueRenderQueue ||
                !(material.shaderKeywords ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal)
                    .SequenceEqual((change.valueShaderKeywords ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal)) return false;
            if (change.valueShader != null)
            {
                var path = AssetDatabase.GetAssetPath(material.shader);
                var guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
                if (guid != change.valueShader.guid && !string.Equals(path, change.valueShader.assetPath, StringComparison.OrdinalIgnoreCase)) return false;
            }
            foreach (var property in change.properties ?? new List<MaterialPropertyChange>())
            {
                if (!property.valueExists || property.value == null || !material.HasProperty(property.name)) continue;
                var value = property.value;
                switch (value.type)
                {
                    case "float": if (Mathf.Abs(material.GetFloat(property.name) - value.floatValue) > Tolerance) return false; break;
                    case "color":
                    case "vector":
                        var vector = material.GetVector(property.name);
                        if (!Approximately(vector, value.vectorValue)) return false;
                        break;
                    case "texture":
                        var currentTexture = material.GetTexture(property.name);
                        if ((currentTexture != null) != value.hasTexture) return false;
                        var texturePath = AssetDatabase.GetAssetPath(currentTexture);
                        var textureGuid = string.IsNullOrEmpty(texturePath) ? string.Empty : AssetDatabase.AssetPathToGUID(texturePath);
                        if (value.texture != null && textureGuid != value.texture.guid && !string.Equals(texturePath, value.texture.assetPath, StringComparison.OrdinalIgnoreCase)) return false;
                        var scale = material.GetTextureScale(property.name);
                        var offset = material.GetTextureOffset(property.name);
                        if (Mathf.Abs(scale.x - value.textureScale.x) > Tolerance || Mathf.Abs(scale.y - value.textureScale.y) > Tolerance ||
                            Mathf.Abs(offset.x - value.textureOffset.x) > Tolerance || Mathf.Abs(offset.y - value.textureOffset.y) > Tolerance) return false;
                        break;
                }
            }
            return true;
        }

        private static bool MatchesImportedMaterial(Material material, MaterialChange change)
        {
            if (material == null || itemShaderMismatch(material, change) ||
                material.renderQueue != change.valueRenderQueue ||
                !(material.shaderKeywords ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal)
                    .SequenceEqual((change.valueShaderKeywords ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal)) return false;
            foreach (var expected in change.valueMaterialState ?? new List<MaterialPropertySnapshot>())
            {
                if (expected == null || !material.HasProperty(expected.name)) return false;
                switch (expected.type)
                {
                    case "float":
                        if (Mathf.Abs(material.GetFloat(expected.name) - expected.floatValue) > Tolerance) return false;
                        break;
                    case "color":
                    case "vector":
                        if (!Approximately(material.GetVector(expected.name), expected.vectorValue)) return false;
                        break;
                    case "texture":
                        var texture = material.GetTexture(expected.name);
                        if ((texture != null) != expected.hasTexture) return false;
                        if (expected.texture != null && !MatchesAsset(texture, expected.texture)) return false;
                        var scale = material.GetTextureScale(expected.name);
                        var offset = material.GetTextureOffset(expected.name);
                        if (Mathf.Abs(scale.x - expected.textureScale.x) > Tolerance || Mathf.Abs(scale.y - expected.textureScale.y) > Tolerance ||
                            Mathf.Abs(offset.x - expected.textureOffset.x) > Tolerance || Mathf.Abs(offset.y - expected.textureOffset.y) > Tolerance) return false;
                        break;
                    default: return false;
                }
            }
            return true;
        }

        private static bool itemShaderMismatch(Material material, MaterialChange change)
        {
            if (change.valueShader == null) return material.shader == null;
            var shaderPath = AssetDatabase.GetAssetPath(material.shader);
            var shaderGuid = string.IsNullOrEmpty(shaderPath) ? string.Empty : AssetDatabase.AssetPathToGUID(shaderPath);
            return material.shader == null || shaderGuid != change.valueShader.guid &&
                   !string.Equals(shaderPath, change.valueShader.assetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesAsset(UnityEngine.Object asset, AssetReference reference)
        {
            if (asset == null || reference == null) return asset == null && reference == null;
            var path = AssetDatabase.GetAssetPath(asset);
            var guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(reference.guid) && guid == reference.guid ||
                   !string.IsNullOrEmpty(reference.assetPath) && string.Equals(path, reference.assetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresImportedMaterialCopy(MaterialChange change)
        {
            if (change == null || change.valueMaterial == null ||
                (change.valueMaterialState == null || change.valueMaterialState.Count == 0)) return false;
            if (change.baselineMaterial == null) return true;
            if (!string.IsNullOrEmpty(change.baselineMaterial.guid) && !string.IsNullOrEmpty(change.valueMaterial.guid))
                return !string.Equals(change.baselineMaterial.guid, change.valueMaterial.guid, StringComparison.Ordinal);
            return !string.Equals(change.baselineMaterial.assetPath, change.valueMaterial.assetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateResults(List<PreparedPrefab> prefabs, List<GameObject> instances,
            List<PreparedTransform> transforms, List<PreparedActive> activeStates, List<PreparedBlendShape> blendShapes,
            List<PreparedMaterial> materials)
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
            foreach (var item in materials)
            {
                var assigned = item.renderer.sharedMaterials[item.change.materialIndex];
                if (!string.IsNullOrEmpty(item.variantPath))
                {
                    if (assigned != AssetDatabase.LoadAssetAtPath<Material>(item.variantPath) || !MatchesVariant(assigned, item.change))
                        throw new InvalidOperationException("Final Material validation failed: " + item.change.target.path);
                }
                else if (assigned != item.valueMaterial)
                    throw new InvalidOperationException("Final Material validation failed: " + item.change.target.path);
            }
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
        private static bool Approximately(Vector4 current, Vector4Value expected) =>
            Mathf.Abs(current.x - expected.x) <= Tolerance && Mathf.Abs(current.y - expected.y) <= Tolerance &&
            Mathf.Abs(current.z - expected.z) <= Tolerance && Mathf.Abs(current.w - expected.w) <= Tolerance;
    }
}
