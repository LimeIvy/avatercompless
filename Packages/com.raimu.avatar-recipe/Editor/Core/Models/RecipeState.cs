using System;
using System.Collections.Generic;

namespace AvatarRecipe.Editor.Core.Models
{
    [Serializable]
    public sealed class RecipeState
    {
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public List<TransformChange> transformChanges = new List<TransformChange>();
        public List<BlendShapeChange> blendShapeChanges = new List<BlendShapeChange>();
        public List<MaterialChange> materialChanges = new List<MaterialChange>();
        public List<ActiveStateChange> activeStateChanges = new List<ActiveStateChange>();
        public List<AddedPrefabEntry> addedPrefabs = new List<AddedPrefabEntry>();
        public List<string> manualReview = new List<string>();

        public bool IsEmpty => transformChanges.Count == 0 && blendShapeChanges.Count == 0 &&
                               materialChanges.Count == 0 && activeStateChanges.Count == 0 &&
                               addedPrefabs.Count == 0 && manualReview.Count == 0;
    }

    [Serializable]
    public sealed class AddedPrefabEntry
    {
        public string id;
        public string name;
        public string guid;
        public string assetPath;
        public string parentScope;
        public string parentPath;
        public int siblingIndex;
        public Vector3Value localPosition;
        public QuaternionValue localRotation;
        public Vector3Value localScale;
    }

    [Serializable]
    public sealed class TransformChange
    {
        public TargetLocator target;
        public string property;
        public Vector3Value baselineVector3;
        public Vector3Value valueVector3;
        public QuaternionValue baselineQuaternion;
        public QuaternionValue valueQuaternion;
    }

    [Serializable]
    public sealed class BlendShapeChange
    {
        public TargetLocator target;
        public string name;
        public float baseline;
        public float value;
    }

    [Serializable]
    public sealed class ActiveStateChange
    {
        public TargetLocator target;
        public bool baseline;
        public bool value;
    }

    [Serializable]
    public sealed class AssetReference
    {
        public string name;
        public string guid;
        public string assetPath;
    }

    [Serializable]
    public sealed class MaterialSlotSnapshot
    {
        public TargetLocator target;
        public int materialIndex;
        public bool hasMaterial;
        public bool hasShader;
        public AssetReference material;
        public AssetReference shader;
        public int renderQueue;
        public string[] shaderKeywords = Array.Empty<string>();
        public List<MaterialPropertySnapshot> properties = new List<MaterialPropertySnapshot>();
    }

    [Serializable]
    public sealed class MaterialPropertySnapshot
    {
        public string name;
        public string type;
        public float floatValue;
        public Vector4Value vectorValue;
        public bool hasTexture;
        public AssetReference texture;
        public Vector4Value textureScale;
        public Vector4Value textureOffset;
    }

    [Serializable]
    public sealed class MaterialChange
    {
        public TargetLocator target;
        public int materialIndex;
        public AssetReference baselineMaterial;
        public AssetReference valueMaterial;
        public AssetReference baselineShader;
        public AssetReference valueShader;
        public int baselineRenderQueue;
        public int valueRenderQueue;
        public string[] baselineShaderKeywords = Array.Empty<string>();
        public string[] valueShaderKeywords = Array.Empty<string>();
        public List<MaterialPropertyChange> properties = new List<MaterialPropertyChange>();
    }

    [Serializable]
    public sealed class MaterialPropertyChange
    {
        public string name;
        public string type;
        public bool baselineExists;
        public bool valueExists;
        public MaterialPropertySnapshot baseline;
        public MaterialPropertySnapshot value;
    }

    [Serializable]
    public struct Vector4Value : IEquatable<Vector4Value>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Vector4Value(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public bool Equals(Vector4Value other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        public override bool Equals(object obj) => obj is Vector4Value other && Equals(other);
        public override int GetHashCode() => (((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ z.GetHashCode()) * 397 ^ w.GetHashCode();
    }
}
