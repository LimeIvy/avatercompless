using System;
using System.Collections.Generic;

namespace AvatarRecipe.Editor.Apply
{
    internal enum CompatibilityStatus
    {
        Found,
        Missing,
        Ambiguous,
        Conflict,
        ManualReview
    }

    [Serializable]
    internal sealed class CompatibilityItem
    {
        public CompatibilityStatus status;
        public string category;
        public string message;
        public string targetPath;
        public string changeKey;
    }

    [Serializable]
    internal sealed class PlannedOperation
    {
        public string kind;
        public string targetPath;
        public string description;
    }

    [Serializable]
    internal sealed class ApplyPlan
    {
        public readonly List<CompatibilityItem> compatibility = new List<CompatibilityItem>();
        public readonly List<PlannedOperation> operations = new List<PlannedOperation>();
        public bool IsFullyCompatible => compatibility.TrueForAll(item => item.status == CompatibilityStatus.Found);
    }
}
