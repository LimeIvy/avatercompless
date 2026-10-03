using System;
using System.Collections.Generic;

namespace AvatarRecipe.Editor.Core.Models
{
    [Serializable]
    public sealed class AvatarSnapshot
    {
        public const int CurrentSchemaVersion = 4;

        public int schemaVersion = CurrentSchemaVersion;
        public List<TransformSnapshot> transforms = new List<TransformSnapshot>();
        public List<BlendShapeSnapshot> blendShapes = new List<BlendShapeSnapshot>();
        public List<MaterialSlotSnapshot> materials = new List<MaterialSlotSnapshot>();
        public List<ActiveSnapshot> activeStates = new List<ActiveSnapshot>();
        public List<AddedPrefabSnapshot> addedPrefabs = new List<AddedPrefabSnapshot>();
        public List<ModularAvatarComponentSnapshot> modularAvatarComponents = new List<ModularAvatarComponentSnapshot>();
    }

    [Serializable]
    public sealed class TransformSnapshot
    {
        public string path;
        public Vector3Value localPosition;
        public QuaternionValue localRotation;
        public Vector3Value localScale;
    }

    [Serializable]
    public sealed class BlendShapeSnapshot
    {
        public TargetLocator target;
        public string name;
        public float weight;
    }

    [Serializable]
    public sealed class ActiveSnapshot
    {
        public string path;
        public bool activeSelf;
    }

    [Serializable]
    public sealed class AddedPrefabSnapshot
    {
        // GlobalObjectId is local baseline identity only. It is never written to a Recipe.
        public string globalObjectId;
        public string path;
        public string sourceName;
        public string sourceGuid;
        public string sourceAssetPath;
        public int siblingIndex;
        public Vector3Value localPosition;
        public QuaternionValue localRotation;
        public Vector3Value localScale;
    }

    [Serializable]
    public sealed class ModularAvatarComponentSnapshot
    {
        public string key;
        public string path;
        public string hostParentPath;
        public int hostSiblingIndex;
        public Vector3Value hostLocalPosition;
        public QuaternionValue hostLocalRotation;
        public Vector3Value hostLocalScale;
        public bool hostActive;
        public string componentType;
        public string componentName;
        public bool enabled;
        public List<ModularAvatarPropertySnapshot> properties = new List<ModularAvatarPropertySnapshot>();
    }

    [Serializable]
    public sealed class ModularAvatarPropertySnapshot
    {
        public string path;
        public string value;
    }

    [Serializable]
    public sealed class TargetLocator
    {
        public const string BaseScope = "base";

        public string scope = BaseScope;
        public string path;
        public string componentId;
    }

    [Serializable]
    public struct Vector3Value : IEquatable<Vector3Value>
    {
        public float x;
        public float y;
        public float z;

        public Vector3Value(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(Vector3Value other)
        {
            return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
        }

        public override bool Equals(object obj)
        {
            return obj is Vector3Value other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ z.GetHashCode();
            }
        }
    }

    [Serializable]
    public struct QuaternionValue : IEquatable<QuaternionValue>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public QuaternionValue(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public bool Equals(QuaternionValue other)
        {
            return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        }

        public override bool Equals(object obj)
        {
            return obj is QuaternionValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = x.GetHashCode();
                hashCode = (hashCode * 397) ^ y.GetHashCode();
                hashCode = (hashCode * 397) ^ z.GetHashCode();
                return (hashCode * 397) ^ w.GetHashCode();
            }
        }
    }
}
