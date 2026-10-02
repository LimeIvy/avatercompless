using System.Collections.Generic;
using AvatarRecipe.Editor.Core.Diff;
using AvatarRecipe.Editor.Core.Snapshot;
using AvatarRecipe.Editor.Scan;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AvatarRecipe.Editor.Tests
{
    public sealed class AddedPrefabTrackingTests
    {
        private string _folder;
        private string _nestedPrefabPath;
        private string _hairPrefabPath;
        private string _scenePath;
        private Scene _scene;
        private Scene _previousActiveScene;
        private readonly List<GameObject> _temporaryObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/__AvatarRecipePrefabTests_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length));
            _nestedPrefabPath = _folder + "/Nested.prefab";
            _hairPrefabPath = _folder + "/Hair.prefab";
            _scenePath = _folder + "/Avatar.unity";
            _previousActiveScene = SceneManager.GetActiveScene();

            var nestedSource = CreateTemporary("NestedAccessory");
            var nestedPrefab = PrefabUtility.SaveAsPrefabAsset(nestedSource, _nestedPrefabPath);
            Object.DestroyImmediate(nestedSource);
            _temporaryObjects.Remove(nestedSource);

            var hairSource = CreateTemporary("Hair");
            var nestedInstance = (GameObject)PrefabUtility.InstantiatePrefab(nestedPrefab);
            nestedInstance.transform.SetParent(hairSource.transform, false);
            var hairPrefab = PrefabUtility.SaveAsPrefabAsset(hairSource, _hairPrefabPath);
            Object.DestroyImmediate(hairSource);
            _temporaryObjects.Remove(hairSource);
            Assert.That(hairPrefab, Is.Not.Null);

            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(_scene);
            _scenePath = _folder + "/Avatar.unity";
            Assert.That(EditorSceneManager.SaveScene(_scene, _scenePath), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _temporaryObjects)
            {
                if (gameObject != null) Object.DestroyImmediate(gameObject);
            }
            _temporaryObjects.Clear();
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
            if (_previousActiveScene.IsValid() && _previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(_previousActiveScene);
            if (!string.IsNullOrEmpty(_folder)) AssetDatabase.DeleteAsset(_folder);
        }

        [Test]
        public void AddedPrefabIsRecordedWithSourceAndPlacementWithoutDoubleCountingNestedPrefab()
        {
            var avatar = CreateTemporary("Avatar");
            Assert.That(EditorSceneManager.SaveScene(_scene, _scenePath), Is.True);
            var baseline = AvatarSnapshotBuilder.Build(avatar);

            var hairAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_hairPrefabPath);
            var hair = (GameObject)PrefabUtility.InstantiatePrefab(hairAsset);
            hair.name = "Hair";
            hair.transform.SetParent(avatar.transform, false);
            hair.transform.localPosition = new Vector3(0.25f, 1.5f, -0.5f);
            hair.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            hair.transform.localScale = new Vector3(1.25f, 1f, 0.75f);
            Assert.That(EditorSceneManager.SaveScene(_scene, _scenePath), Is.True);

            var current = AvatarSnapshotBuilder.Build(avatar);
            var delta = AvatarDiffEngine.Diff(baseline, current);

            Assert.That(current.addedPrefabs, Has.Count.EqualTo(1));
            Assert.That(delta.addedPrefabs, Has.Count.EqualTo(1));
            Assert.That(delta.addedPrefabs[0].name, Is.EqualTo("Hair"));
            Assert.That(delta.addedPrefabs[0].guid, Is.EqualTo(AssetDatabase.AssetPathToGUID(_hairPrefabPath)));
            Assert.That(delta.addedPrefabs[0].assetPath, Is.EqualTo(_hairPrefabPath));
            Assert.That(delta.addedPrefabs[0].parentScope, Is.EqualTo("base"));
            Assert.That(delta.addedPrefabs[0].parentPath, Is.Empty);
            Assert.That(delta.addedPrefabs[0].siblingIndex, Is.EqualTo(0));
            Assert.That(delta.addedPrefabs[0].localPosition.x, Is.EqualTo(0.25f));
            Assert.That(delta.addedPrefabs[0].localPosition.y, Is.EqualTo(1.5f));
            Assert.That(delta.addedPrefabs[0].localRotation.y, Is.EqualTo(0.25882f));
            Assert.That(delta.addedPrefabs[0].localScale.z, Is.EqualTo(0.75f));
            Assert.That(delta.transformChanges, Is.Empty);
        }

        [Test]
        public void TrackAndScanInputsProduceEquivalentRecipeState()
        {
            var originalPath = _folder + "/OriginalAvatar.prefab";
            var originalSource = CreateTemporary("OriginalAvatar");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(originalSource.transform, false);
            var originalAsset = PrefabUtility.SaveAsPrefabAsset(originalSource, originalPath);
            Object.DestroyImmediate(originalSource);
            _temporaryObjects.Remove(originalSource);

            var modified = (GameObject)PrefabUtility.InstantiatePrefab(originalAsset);
            modified.name = "ModifiedAvatar";
            Assert.That(EditorSceneManager.SaveScene(_scene, _scenePath), Is.True);
            var trackBaseline = AvatarSnapshotBuilder.Build(modified);

            var modifiedBone = modified.transform.Find("Bone");
            modifiedBone.localPosition = new Vector3(0f, 0.75f, 0f);
            var hairAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_hairPrefabPath);
            var hair = (GameObject)PrefabUtility.InstantiatePrefab(hairAsset);
            hair.transform.SetParent(modified.transform, false);
            hair.transform.localPosition = Vector3.right;
            Assert.That(EditorSceneManager.SaveScene(_scene, _scenePath), Is.True);
            var current = AvatarSnapshotBuilder.Build(modified);

            var trackResult = AvatarDiffEngine.Diff(trackBaseline, current);
            var scanResult = ExistingAvatarScanner.Compare(modified, originalAsset);

            Assert.That(JsonUtility.ToJson(scanResult), Is.EqualTo(JsonUtility.ToJson(trackResult)));
            Assert.That(scanResult.addedPrefabs, Has.Count.EqualTo(1));
            Assert.That(scanResult.transformChanges, Has.Count.EqualTo(1));
        }

        private GameObject CreateTemporary(string name)
        {
            var gameObject = new GameObject(name);
            _temporaryObjects.Add(gameObject);
            return gameObject;
        }
    }
}
