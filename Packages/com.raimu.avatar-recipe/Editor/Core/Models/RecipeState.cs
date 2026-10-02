using System;
using System.Collections.Generic;

namespace AvatarRecipe.Editor.Core.Models
{
    [Serializable]
    public sealed class RecipeState
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public List<TransformChange> transformChanges = new List<TransformChange>();
        public List<BlendShapeChange> blendShapeChanges = new List<BlendShapeChange>();
        public List<ActiveStateChange> activeStateChanges = new List<ActiveStateChange>();
        public List<AddedPrefabEntry> addedPrefabs = new List<AddedPrefabEntry>();
        public List<string> manualReview = new List<string>();

        public bool IsEmpty => transformChanges.Count == 0 && blendShapeChanges.Count == 0 &&
                               activeStateChanges.Count == 0 && addedPrefabs.Count == 0 && manualReview.Count == 0;
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
}
