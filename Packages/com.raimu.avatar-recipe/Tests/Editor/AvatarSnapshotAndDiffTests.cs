using System;
using System.Collections.Generic;
using AvatarRecipe.Editor.Core.Diff;
using AvatarRecipe.Editor.Core.Models;
using AvatarRecipe.Editor.Core.Snapshot;
using NUnit.Framework;
using UnityEngine;

namespace AvatarRecipe.Editor.Tests
{
    public sealed class AvatarSnapshotAndDiffTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            foreach (var root in _roots)
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            foreach (var mesh in _meshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            _roots.Clear();
            _meshes.Clear();
        }

        [Test]
        public void BuilderCapturesTransformsBlendShapesAndInactiveObjects()
        {
            var avatar = CreateAvatar();
            avatar.root.transform.localPosition = new Vector3(1.234567f, 2f, 3f);
            avatar.body.SetActive(false);
            avatar.renderer.SetBlendShapeWeight(0, 37.123456f);

            var snapshot = AvatarSnapshotBuilder.Build(avatar.root);

            Assert.That(snapshot.transforms.Count, Is.EqualTo(2));
            Assert.That(snapshot.transforms[0].path, Is.EqualTo(string.Empty));
            Assert.That(snapshot.transforms[0].localPosition.x, Is.EqualTo(1.23457f));
            Assert.That(snapshot.transforms[1].path, Is.EqualTo("Body"));
            Assert.That(snapshot.activeStates[1].activeSelf, Is.False);
            Assert.That(snapshot.blendShapes.Count, Is.EqualTo(1));
            Assert.That(snapshot.blendShapes[0].target.path, Is.EqualTo("Body"));
            Assert.That(snapshot.blendShapes[0].target.componentId, Is.EqualTo("SkinnedMeshRenderer:0"));
            Assert.That(snapshot.blendShapes[0].name, Is.EqualTo("Smile"));
            Assert.That(snapshot.blendShapes[0].weight, Is.EqualTo(37.12346f));
        }

        [Test]
        public void BuilderRejectsAmbiguousDuplicateHierarchyPaths()
        {
            var root = CreateRoot("Avatar");
            CreateChild(root.transform, "Bone");
            CreateChild(root.transform, "Bone");

            Assert.Throws<InvalidOperationException>(() => AvatarSnapshotBuilder.Build(root));
        }

        [Test]
        public void DiffReturnsEmptyStateWhenSnapshotsAreEqual()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            var current = AvatarSnapshotBuilder.Build(avatar.root);

            var delta = AvatarDiffEngine.Diff(baseline, current);

            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void DiffRecordsSupportedChangesInStableOrder()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);

            avatar.root.transform.localPosition = new Vector3(1f, 0f, 0f);
            avatar.body.transform.localScale = new Vector3(2f, 1f, 1f);
            avatar.body.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
            avatar.body.SetActive(false);
            avatar.renderer.SetBlendShapeWeight(0, 35f);

            var current = AvatarSnapshotBuilder.Build(avatar.root);
            baseline.transforms.Reverse();
            baseline.blendShapes.Reverse();
            baseline.activeStates.Reverse();
            current.transforms.Reverse();
            current.blendShapes.Reverse();
            current.activeStates.Reverse();

            var delta = AvatarDiffEngine.Diff(baseline, current);

            Assert.That(delta.transformChanges.Count, Is.EqualTo(3));
            Assert.That(delta.transformChanges[0].target.path, Is.EqualTo(string.Empty));
            Assert.That(delta.transformChanges[0].property, Is.EqualTo("localPosition"));
            Assert.That(delta.transformChanges[1].target.path, Is.EqualTo("Body"));
            Assert.That(delta.transformChanges[1].property, Is.EqualTo("localRotation"));
            Assert.That(delta.transformChanges[2].property, Is.EqualTo("localScale"));
            Assert.That(delta.blendShapeChanges.Count, Is.EqualTo(1));
            Assert.That(delta.blendShapeChanges[0].name, Is.EqualTo("Smile"));
            Assert.That(delta.blendShapeChanges[0].baseline, Is.EqualTo(0f));
            Assert.That(delta.blendShapeChanges[0].value, Is.EqualTo(35f));
            Assert.That(delta.activeStateChanges.Count, Is.EqualTo(1));
            Assert.That(delta.activeStateChanges[0].target.path, Is.EqualTo("Body"));
            Assert.That(delta.activeStateChanges[0].baseline, Is.True);
            Assert.That(delta.activeStateChanges[0].value, Is.False);
        }

        [Test]
        public void ReturningValuesToBaselineRemovesAllDeltaEntries()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            avatar.root.transform.localPosition = Vector3.one;
            avatar.body.SetActive(false);
            avatar.renderer.SetBlendShapeWeight(0, 50f);
            Assert.That(AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root)).IsEmpty, Is.False);

            avatar.root.transform.localPosition = Vector3.zero;
            avatar.body.SetActive(true);
            avatar.renderer.SetBlendShapeWeight(0, 0f);

            Assert.That(AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root)).IsEmpty, Is.True);
        }

        [Test]
        public void DiffRecordsInactiveToActiveChanges()
        {
            var avatar = CreateAvatar();
            avatar.body.SetActive(false);
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            avatar.body.SetActive(true);

            var delta = AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root));

            Assert.That(delta.activeStateChanges.Count, Is.EqualTo(1));
            Assert.That(delta.activeStateChanges[0].baseline, Is.False);
            Assert.That(delta.activeStateChanges[0].value, Is.True);
        }

        [Test]
        public void BlendShapesAreMatchedByNameInsteadOfMeshIndex()
        {
            var avatar = CreateAvatar();
            var baselineMesh = CreateBlendShapeMesh("Smile", "Blink");
            avatar.renderer.sharedMesh = baselineMesh;
            avatar.renderer.SetBlendShapeWeight(0, 25f);
            avatar.renderer.SetBlendShapeWeight(1, 75f);
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);

            avatar.renderer.sharedMesh = CreateBlendShapeMesh("Blink", "Smile");
            avatar.renderer.SetBlendShapeWeight(0, 75f);
            avatar.renderer.SetBlendShapeWeight(1, 25f);

            var delta = AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root));

            Assert.That(delta.blendShapeChanges, Is.Empty);
        }

        [Test]
        public void NormalizationRemovesSmallFloatingPointNoise()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            avatar.root.transform.localPosition = new Vector3(0.000001f, 0f, 0f);
            avatar.renderer.SetBlendShapeWeight(0, 0.000001f);

            var delta = AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root));

            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void DiffReportsNonPrefabHierarchyChangesForManualReview()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            CreateChild(avatar.root.transform, "Extra");

            var delta = AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root));

            Assert.That(delta.manualReview, Has.Some.Contains("Added object has no source Prefab"));
            Assert.That(delta.transformChanges, Is.Empty);
        }

        [Test]
        public void DiffKeepsSupportedChangesAndReportsRemovedObjectsForManualReview()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            avatar.root.transform.localPosition = Vector3.right;
            UnityEngine.Object.DestroyImmediate(avatar.body);

            var delta = AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root));

            Assert.That(delta.transformChanges, Has.Count.EqualTo(1));
            Assert.That(delta.transformChanges[0].property, Is.EqualTo("localPosition"));
            Assert.That(delta.manualReview, Has.Some.Contains("Removed base object requires manual review: Body"));
        }

        [Test]
        public void DiffReportsBlendShapeLayoutChangesForManualReview()
        {
            var avatar = CreateAvatar();
            var baseline = AvatarSnapshotBuilder.Build(avatar.root);
            avatar.renderer.sharedMesh = CreateBlendShapeMesh("Blink");

            var delta = AvatarDiffEngine.Diff(baseline, AvatarSnapshotBuilder.Build(avatar.root));

            Assert.That(delta.manualReview, Has.Some.Contains("Added BlendShape is not included in MVP reconstruction: Body/Blink"));
            Assert.That(delta.manualReview, Has.Some.Contains("Removed BlendShape requires manual review: Body/Smile"));
        }

        private AvatarObjects CreateAvatar()
        {
            var root = CreateRoot("Avatar");
            var body = CreateChild(root.transform, "Body");
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            var mesh = CreateBlendShapeMesh("Smile");
            renderer.sharedMesh = mesh;
            return new AvatarObjects(root, body, renderer);
        }

        private GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            _roots.Add(root);
            return root;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private Mesh CreateBlendShapeMesh(params string[] shapeNames)
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f)
                }
            };
            foreach (var shapeName in shapeNames)
            {
                mesh.AddBlendShapeFrame(shapeName, 100f,
                    new[] { Vector3.up, Vector3.zero, Vector3.zero },
                    new Vector3[3],
                    new Vector3[3]);
            }
            _meshes.Add(mesh);
            return mesh;
        }

        private sealed class AvatarObjects
        {
            public readonly GameObject root;
            public readonly GameObject body;
            public readonly SkinnedMeshRenderer renderer;

            public AvatarObjects(GameObject root, GameObject body, SkinnedMeshRenderer renderer)
            {
                this.root = root;
                this.body = body;
                this.renderer = renderer;
            }
        }
    }
}
