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
            foreach (var change in recipe.state.materialChanges ?? new List<MaterialChange>())
                PlanMaterial(target.transform, target, change, plan);
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

        private static void PlanMaterial(Transform root, GameObject avatar, MaterialChange change, ApplyPlan plan)
        {
            var target = ResolvePath(root, change.target.path, out var ambiguous);
            if (target == null)
            {
                AddTargetIssue(plan, "Material", change.target.path, ambiguous);
                return;
            }
            if (!TryGetRendererIndex(change.target.componentId, out var rendererIndex))
            {
                AddTargetIssue(plan, "Material", change.target.path + "/" + change.target.componentId, false);
                return;
            }
            var renderers = target.GetComponents<Renderer>();
            if (rendererIndex >= renderers.Length || renderers[rendererIndex] == null)
            {
                AddTargetIssue(plan, "Material", change.target.path + "/" + change.target.componentId, false);
                return;
            }
            var renderer = renderers[rendererIndex];
            var materials = renderer.sharedMaterials;
            if (change.materialIndex < 0 || change.materialIndex >= materials.Length)
            {
                AddTargetIssue(plan, "Material Slot", change.target.path + " [" + change.materialIndex + "]", false);
                return;
            }

            var current = materials[change.materialIndex];
            var sameMaterialReference = SameAsset(change.baselineMaterial, change.valueMaterial);
            var materialSettingsChanged = change.baselineRenderQueue != change.valueRenderQueue ||
                !(change.baselineShaderKeywords ?? Array.Empty<string>()).SequenceEqual(change.valueShaderKeywords ?? Array.Empty<string>(), StringComparer.Ordinal);
            var variantPath = change.baselineMaterial != null && SameAsset(change.baselineMaterial, change.valueMaterial) &&
                              ((change.properties ?? new List<MaterialPropertyChange>()).Count > 0 || !SameAsset(change.baselineShader, change.valueShader) || materialSettingsChanged)
                ? GetMaterialVariantPath(avatar, change)
                : string.Empty;
            var generatedVariant = string.IsNullOrEmpty(variantPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(variantPath);
            var isAtValue = (sameMaterialReference
                    ? MatchesMaterialState(current, change, true)
                    : MatchesMaterialAssignment(current, change.valueMaterial)) ||
                (generatedVariant != null && current == generatedVariant && MatchesShader(current.shader, change.valueShader) &&
                 MatchesProperties(current, change.properties, true) && MatchesMaterialSettings(current, change, true));
            var isAtBaseline = sameMaterialReference
                ? MatchesMaterialState(current, change, false)
                : MatchesMaterialAssignment(current, change.baselineMaterial);
            if (isAtValue)
            {
                plan.compatibility.Add(new CompatibilityItem
                {
                    status = CompatibilityStatus.Found,
                    category = "Material",
                    message = AvatarRecipeLocalization.Format("Material already matches the Recipe at {0} (slot {1})", DisplayPath(change.target.path), change.materialIndex),
                    targetPath = change.target.path,
                    changeKey = MaterialKey(change)
                });
                return;
            }

            var operationValue = ResolveMaterialOperation(change, out var valueMaterial, out var shader, out var missingReference);
            if (!operationValue)
            {
                plan.compatibility.Add(new CompatibilityItem
                {
                    status = CompatibilityStatus.Missing,
                    category = "Material",
                    message = AvatarRecipeLocalization.Format("Could not resolve material or shader asset: {0}", missingReference),
                    targetPath = change.target.path,
                    changeKey = MaterialKey(change)
                });
                return;
            }

            if (!string.IsNullOrEmpty(variantPath))
            {
                var occupied = AssetDatabase.LoadMainAssetAtPath(variantPath);
                if (occupied != null && (!(occupied is Material generated) ||
                    !MatchesShader(generated.shader, change.valueShader) || !MatchesProperties(generated, change.properties, true) ||
                    !MatchesMaterialSettings(generated, change, true)))
                {
                    plan.compatibility.Add(new CompatibilityItem
                    {
                        status = CompatibilityStatus.Missing,
                        category = "Material",
                        message = AvatarRecipeLocalization.Format("Generated Material path is occupied by a different asset: {0}", variantPath),
                        targetPath = change.target.path,
                        changeKey = MaterialKey(change)
                    });
                    return;
                }
            }

            var conflict = !isAtBaseline;
            plan.compatibility.Add(new CompatibilityItem
            {
                status = conflict ? CompatibilityStatus.Conflict : CompatibilityStatus.Found,
                category = "Material",
                message = conflict
                    ? AvatarRecipeLocalization.Format("Current material differs from the Recipe baseline at {0} (slot {1})", DisplayPath(change.target.path), change.materialIndex)
                    : AvatarRecipeLocalization.Format("Material target found at {0} (slot {1})", DisplayPath(change.target.path), change.materialIndex),
                targetPath = change.target.path,
                changeKey = MaterialKey(change)
            });
            var description = AvatarRecipeLocalization.Format("Slot {0}: {1}", change.materialIndex,
                change.valueMaterial == null ? "None" : change.valueMaterial.name);
            if (sameMaterialReference && !SameAsset(change.baselineShader, change.valueShader))
                description += "; " + AvatarRecipeLocalization.Format("Shader: {0}", change.valueShader == null ? "None" : change.valueShader.name);
            if (change.properties != null && change.properties.Count > 0)
                description += "; " + AvatarRecipeLocalization.Format("Changed properties: {0}", string.Join(", ", change.properties.Select(item => item.name)));
            if (change.baselineRenderQueue != change.valueRenderQueue)
                description += "; " + AvatarRecipeLocalization.Format("Render Queue: {0} → {1}", change.baselineRenderQueue, change.valueRenderQueue);
            if (!(change.baselineShaderKeywords ?? Array.Empty<string>()).SequenceEqual(change.valueShaderKeywords ?? Array.Empty<string>(), StringComparer.Ordinal))
                description += "; " + AvatarRecipeLocalization.Format("Shader Keywords: {0} → {1}",
                    string.Join(", ", change.baselineShaderKeywords ?? Array.Empty<string>()),
                    string.Join(", ", change.valueShaderKeywords ?? Array.Empty<string>()));
            if (!string.IsNullOrEmpty(variantPath))
                description += "; " + AvatarRecipeLocalization.Get("Avatar-specific Material will be generated");
            plan.operations.Add(new PlannedOperation
            {
                kind = "Material Change",
                targetPath = DisplayPath(change.target.path),
                description = description,
                changeKey = MaterialKey(change),
                materialChange = change,
                renderer = renderer,
                valueMaterial = valueMaterial,
                valueShader = shader,
                variantPath = variantPath
            });
        }

        private static bool ResolveMaterialOperation(MaterialChange change, out Material valueMaterial,
            out Shader valueShader, out string missingReference)
        {
            valueMaterial = null;
            valueShader = null;
            missingReference = string.Empty;
            if (change.valueMaterial != null)
            {
                valueMaterial = ResolveAsset<Material>(change.valueMaterial);
                if (valueMaterial == null)
                {
                    missingReference = change.valueMaterial.assetPath;
                    return false;
                }
            }
            var sameMaterialReference = SameAsset(change.baselineMaterial, change.valueMaterial);
            if (sameMaterialReference && change.valueShader != null)
            {
                valueShader = ResolveAsset<Shader>(change.valueShader);
                if (valueShader == null)
                {
                    missingReference = change.valueShader.assetPath;
                    return false;
                }
            }
            foreach (var property in change.properties ?? new List<MaterialPropertyChange>())
            {
                var value = property.valueExists ? property.value : null;
                if (value == null || value.type != "texture" || value.texture == null) continue;
                if (ResolveAsset<Texture>(value.texture) == null)
                {
                    missingReference = value.texture.assetPath;
                    return false;
                }
            }
            return true;
        }

        private static T ResolveAsset<T>(AssetReference reference) where T : UnityEngine.Object
        {
            if (reference == null) return null;
            if (!string.IsNullOrEmpty(reference.guid))
            {
                var byGuid = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(reference.guid));
                if (byGuid != null) return byGuid;
            }
            return string.IsNullOrEmpty(reference.assetPath) ? null : AssetDatabase.LoadAssetAtPath<T>(reference.assetPath);
        }

        private static bool MatchesMaterialState(Material material, MaterialChange change, bool useValue)
        {
            var expectedMaterial = useValue ? change.valueMaterial : change.baselineMaterial;
            var expectedShader = useValue ? change.valueShader : change.baselineShader;
            if (expectedMaterial == null) return material == null;
            if (material == null || !SameAsset(GetAssetReference(material), expectedMaterial)) return false;
            if (!MatchesShader(material.shader, expectedShader)) return false;
            return MatchesProperties(material, change.properties, useValue) && MatchesMaterialSettings(material, change, useValue);
        }

        private static bool MatchesMaterialSettings(Material material, MaterialChange change, bool useValue)
        {
            var renderQueue = useValue ? change.valueRenderQueue : change.baselineRenderQueue;
            var shaderKeywords = useValue ? change.valueShaderKeywords : change.baselineShaderKeywords;
            return material.renderQueue == renderQueue &&
            (material.shaderKeywords ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal)
                .SequenceEqual((shaderKeywords ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal);
        }

        private static bool MatchesMaterialAssignment(Material material, AssetReference expectedMaterial) =>
            expectedMaterial == null ? material == null : material != null && SameAsset(GetAssetReference(material), expectedMaterial);

        private static bool MatchesShader(Shader shader, AssetReference expected)
        {
            if (expected == null) return shader == null;
            return shader != null && SameAsset(GetAssetReference(shader), expected);
        }

        private static bool MatchesProperties(Material material, List<MaterialPropertyChange> changes, bool useValue)
        {
            foreach (var change in changes ?? new List<MaterialPropertyChange>())
            {
                var exists = useValue ? change.valueExists : change.baselineExists;
                var expected = useValue ? change.value : change.baseline;
                if (!exists)
                {
                    if (material.HasProperty(change.name)) return false;
                    continue;
                }
                if (expected == null || !material.HasProperty(change.name)) return false;
                switch (expected.type)
                {
                    case "float":
                        if (Mathf.Abs(material.GetFloat(change.name) - expected.floatValue) > Tolerance) return false;
                        break;
                    case "color":
                    case "vector":
                        var vector = material.GetVector(change.name);
                        if (!Approximately(vector, expected.vectorValue)) return false;
                        break;
                    case "texture":
                        var currentTexture = material.GetTexture(change.name);
                        if ((currentTexture != null) != expected.hasTexture ||
                            !SameAsset(GetAssetReference(currentTexture), expected.texture)) return false;
                        var scale = material.GetTextureScale(change.name);
                        var offset = material.GetTextureOffset(change.name);
                        if (Mathf.Abs(scale.x - expected.textureScale.x) > Tolerance || Mathf.Abs(scale.y - expected.textureScale.y) > Tolerance ||
                            Mathf.Abs(offset.x - expected.textureOffset.x) > Tolerance || Mathf.Abs(offset.y - expected.textureOffset.y) > Tolerance) return false;
                        break;
                    default: return false;
                }
            }
            return true;
        }

        private static AssetReference GetAssetReference(UnityEngine.Object asset)
        {
            if (asset == null) return null;
            var path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : new AssetReference
            {
                name = asset.name,
                guid = AssetDatabase.AssetPathToGUID(path),
                assetPath = path.Replace('\\', '/')
            };
        }

        private static bool SameAsset(AssetReference left, AssetReference right)
        {
            var leftIsEmpty = IsEmptyAssetReference(left);
            var rightIsEmpty = IsEmptyAssetReference(right);
            if (leftIsEmpty || rightIsEmpty) return leftIsEmpty && rightIsEmpty;
            if (!string.IsNullOrEmpty(left.guid) && !string.IsNullOrEmpty(right.guid)) return left.guid == right.guid;
            return string.Equals((left.assetPath ?? string.Empty).Replace('\\', '/'),
                (right.assetPath ?? string.Empty).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmptyAssetReference(AssetReference asset) => asset == null ||
            string.IsNullOrEmpty(asset.guid) && string.IsNullOrEmpty(asset.assetPath);

        private static string MaterialKey(MaterialChange change) =>
            "material|" + change.target.path + "|" + change.target.componentId + "|" + change.materialIndex;

        private static string GetMaterialVariantPath(GameObject avatar, MaterialChange change)
        {
            var source = change.baselineMaterial == null ? "Material" : change.baselineMaterial.name;
            var safeSource = string.Concat(source.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_'));
            var identity = UnityEngine.JsonUtility.ToJson(change);
            var hash = System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(identity));
            var suffix = string.Concat(hash.Take(6).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            var safeName = string.Concat(avatar.name.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_'));
            return "Assets/AvatarRecipeGenerated/Materials/" + safeName + "_" + safeSource + "_" + suffix + ".mat";
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
            const string rendererPrefix = "Renderer:";
            const string skinnedPrefix = "SkinnedMeshRenderer:";
            if (componentId != null &&
                (componentId.StartsWith(rendererPrefix, StringComparison.Ordinal) || componentId.StartsWith(skinnedPrefix, StringComparison.Ordinal)))
            {
                var prefix = componentId.StartsWith(rendererPrefix, StringComparison.Ordinal) ? rendererPrefix : skinnedPrefix;
                if (int.TryParse(componentId.Substring(prefix.Length), out index)) return true;
            }
            index = -1;
            return false;
        }

        private static bool Approximately(Vector3 current, Vector3Value expected) =>
            Mathf.Abs(current.x - expected.x) <= Tolerance && Mathf.Abs(current.y - expected.y) <= Tolerance && Mathf.Abs(current.z - expected.z) <= Tolerance;
        private static bool Approximately(Quaternion current, QuaternionValue expected) =>
            Mathf.Abs(Mathf.Abs(Quaternion.Dot(current.normalized, new Quaternion(expected.x, expected.y, expected.z, expected.w).normalized)) - 1f) <= Tolerance;
        private static bool Approximately(Vector4 current, Vector4Value expected) =>
            Mathf.Abs(current.x - expected.x) <= Tolerance && Mathf.Abs(current.y - expected.y) <= Tolerance &&
            Mathf.Abs(current.z - expected.z) <= Tolerance && Mathf.Abs(current.w - expected.w) <= Tolerance;
        private static bool Approximately(float current, float expected) => Mathf.Abs(current - expected) <= Tolerance;
        private static string DisplayPath(string path) => string.IsNullOrEmpty(path) ? "<Avatar Root>" : path;
    }
}
