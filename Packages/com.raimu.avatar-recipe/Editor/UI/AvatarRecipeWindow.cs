using System;
using System.IO;
using System.Linq;
using AvatarRecipe.Editor.Apply;
using AvatarRecipe.Editor.Localization;
using AvatarRecipe.Editor.Scan;
using AvatarRecipe.Editor.Settings;
using AvatarRecipe.Editor.Tracking;
using UnityEditor;
using UnityEngine;

namespace AvatarRecipe.Editor.UI
{
    internal sealed class AvatarRecipeWindow : EditorWindow
    {
        private GameObject _selectedRoot;
        private GameObject _modifiedAvatar;
        private GameObject _originalAvatarPrefab;
        private GameObject _previewTarget;
        private string _recipeStatePath;
        private ApplyPlan _previewPlan;
        private Vector2 _scrollPosition;
        private string _statusMessage;
        private MessageType _statusType = MessageType.Info;

        [MenuItem("Window/Avatar Recipe")]
        private static void Open()
        {
            var window = GetWindow<AvatarRecipeWindow>();
            window.titleContent = new GUIContent("Avatar Recipe");
            window.minSize = new Vector2(420, 420);
            window.Show();
        }

        private void OnEnable()
        {
            AvatarTrackingService.Changed += Repaint;
            _recipeStatePath = Path.Combine(AvatarRecipeProjectSettings.GetRecipeProjectDirectory(), "recipes", "Avatar", "state.json");
        }
        private void OnDisable() => AvatarTrackingService.Changed -= Repaint;

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Avatar Recipe", EditorStyles.largeLabel);
            DrawLanguageSelector();
            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Project Settings"), EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(AvatarRecipeProjectSettings.LoadError))
            {
                EditorGUILayout.HelpBox(AvatarRecipeLocalization.Translate(AvatarRecipeProjectSettings.LoadError), MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(AvatarRecipeLocalization.Get("Project ID"), AvatarRecipeProjectSettings.ProjectId);
                EditorGUILayout.TextField(AvatarRecipeLocalization.Get("Unity Project"), AvatarRecipeProjectSettings.ProjectName);
            }

            DrawRecipeRoot();
            EditorGUILayout.Space(14);
            DrawTracking();
            EditorGUILayout.Space(14);
            DrawScan();
            EditorGUILayout.Space(14);
            DrawCompatibilityPreview();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawLanguageSelector()
        {
            var previous = AvatarRecipeLocalization.LanguageChoice;
            var selected = EditorGUILayout.Popup(new GUIContent(AvatarRecipeLocalization.Get("Language"),
                    AvatarRecipeLocalization.Get("Choose the UI language. Auto follows the system language; your choice is saved for this Editor user.")), previous,
                AvatarRecipeLocalization.LanguageOptions);
            if (selected == previous) return;
            AvatarRecipeLocalization.SetLanguageChoice(selected);
            _previewPlan = null;
            _statusMessage = null;
            Repaint();
        }

        private void DrawCompatibilityPreview()
        {
            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Import Recipe"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("Select a target Avatar and a Recipe state.json. Review the compatibility and planned changes before applying; building this preview does not modify the scene."), MessageType.None);
            EditorGUI.BeginChangeCheck();
            _previewTarget = (GameObject)EditorGUILayout.ObjectField(AvatarRecipeLocalization.Get("Target Avatar"), _previewTarget, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) _previewPlan = null;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _recipeStatePath = EditorGUILayout.TextField(AvatarRecipeLocalization.Get("Recipe state.json"), _recipeStatePath ?? string.Empty);
                if (EditorGUI.EndChangeCheck()) _previewPlan = null;
                if (GUILayout.Button(AvatarRecipeLocalization.Get("Browse"), GUILayout.Width(72)))
                {
                    var startDirectory = string.IsNullOrEmpty(_recipeStatePath)
                        ? Path.Combine(AvatarRecipeProjectSettings.GetRecipeProjectDirectory(), "recipes", "Avatar")
                        : Path.GetDirectoryName(_recipeStatePath);
                    if (string.IsNullOrEmpty(startDirectory) || !Directory.Exists(startDirectory))
                        startDirectory = AvatarRecipeProjectSettings.RecipeRoot;
                    var selected = EditorUtility.OpenFilePanel(AvatarRecipeLocalization.Get("Select Recipe state.json"), startDirectory, "json");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        _recipeStatePath = selected;
                        _previewPlan = null;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(_previewTarget == null || string.IsNullOrWhiteSpace(_recipeStatePath)))
            {
                if (GUILayout.Button(AvatarRecipeLocalization.Get("Review Apply Plan")))
                {
                    try
                    {
                        var recipe = RecipeLoader.Load(_recipeStatePath);
                        _previewPlan = ApplyPlanner.Build(recipe, _previewTarget);
                        _statusMessage = AvatarRecipeLocalization.Get("Preview created. The target scene was not modified.");
                        _statusType = _previewPlan.IsFullyCompatible ? MessageType.Info : MessageType.Warning;
                    }
                    catch (Exception exception) { SetError(exception); }
                }
            }

            if (_previewPlan == null) return;
            var displayedPlan = _previewPlan;
            EditorGUILayout.HelpBox(displayedPlan.IsFullyCompatible
                ? AvatarRecipeLocalization.Get("Compatibility check complete. All reported targets are resolvable; no scene changes were made.")
                : AvatarRecipeLocalization.Get("Compatibility check complete. Review missing, ambiguous, conflicting, and manual review items before applying."),
                displayedPlan.IsFullyCompatible ? MessageType.Info : MessageType.Warning);
            foreach (var item in displayedPlan.compatibility)
            {
                var type = item.status == CompatibilityStatus.Found ? MessageType.Info :
                    item.status == CompatibilityStatus.Conflict || item.status == CompatibilityStatus.ManualReview
                        ? MessageType.Warning : MessageType.Error;
                EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get(item.category) + " — " +
                    AvatarRecipeLocalization.Get(item.status.ToString()) + ": " + AvatarRecipeLocalization.Translate(item.message), type);
            }
            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Apply Plan"), EditorStyles.boldLabel);
            if (displayedPlan.operations.Count == 0) EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("No supported operations."));
            var blocked = displayedPlan.compatibility.Any(item =>
                item.status == CompatibilityStatus.Missing || item.status == CompatibilityStatus.Ambiguous);
            using (new EditorGUI.DisabledScope(blocked || displayedPlan.operations.Count == 0))
            {
                if (GUILayout.Button(AvatarRecipeLocalization.Get("Apply Previewed Recipe"))) ApplyPreviewedRecipe();
            }
            foreach (var operation in displayedPlan.operations)
                EditorGUILayout.LabelField("• " + AvatarRecipeLocalization.Get(operation.kind) + " — " + operation.targetPath + " — " + AvatarRecipeLocalization.Translate(operation.description),
                    EditorStyles.wordWrappedLabel);
        }

        private void ApplyPreviewedRecipe()
        {
            try
            {
                var recipe = RecipeLoader.Load(_recipeStatePath);
                var plan = ApplyPlanner.Build(recipe, _previewTarget);
                _previewPlan = plan;
                if (plan.compatibility.Any(item => item.status == CompatibilityStatus.Missing ||
                                                   item.status == CompatibilityStatus.Ambiguous))
                {
                    _statusMessage = AvatarRecipeLocalization.Get("Apply is blocked. Resolve all missing or ambiguous items, then build a new preview.");
                    _statusType = MessageType.Error;
                    return;
                }

                var conflicts = plan.compatibility.Count(item => item.status == CompatibilityStatus.Conflict);
                var manualReview = plan.compatibility.Count(item => item.status == CompatibilityStatus.ManualReview);
                var skipConflicts = false;
                bool confirmed;
                if (conflicts > 0)
                {
                    var choice = EditorUtility.DisplayDialogComplex(AvatarRecipeLocalization.Get("Recipe Conflicts"),
                        AvatarRecipeLocalization.Format("{0} conflict(s) were found. Apply Anyway overwrites conflicting values. Skip Conflicts leaves conflicting values unchanged. Manual Review items are always skipped.", conflicts),
                        AvatarRecipeLocalization.Get("Apply Anyway"), AvatarRecipeLocalization.Get("Skip Conflicts"), AvatarRecipeLocalization.Get("Cancel"));
                    if (choice == 2) return;
                    skipConflicts = choice == 1;
                    confirmed = true;
                }
                else if (manualReview > 0)
                {
                    confirmed = EditorUtility.DisplayDialog(AvatarRecipeLocalization.Get("Manual Review Required"),
                        AvatarRecipeLocalization.Format("{0} unsupported or manual review item(s) will be skipped. Apply the supported operations?", manualReview),
                        AvatarRecipeLocalization.Get("Apply Supported"), AvatarRecipeLocalization.Get("Cancel"));
                }
                else
                {
                    confirmed = EditorUtility.DisplayDialog(AvatarRecipeLocalization.Get("Apply Recipe"),
                        AvatarRecipeLocalization.Format("Apply {0} planned operation(s)? One Undo will revert this Apply.", plan.operations.Count),
                        AvatarRecipeLocalization.Get("Apply"), AvatarRecipeLocalization.Get("Cancel"));
                }

                if (!confirmed) return;
                var result = RecipeApplier.Apply(recipe, _previewTarget, plan, skipConflicts);
                _previewPlan = null;
                _statusMessage = AvatarRecipeLocalization.Format("Applied {0} operation(s). {1} conflict(s) skipped; {2} manual review item(s) left unresolved. Save the scene to keep the changes. One Undo reverts the Apply.",
                    result.appliedOperations, result.skippedConflicts, result.manualReviewItems);
                _statusType = result.manualReviewItems > 0 || result.skippedConflicts > 0
                    ? MessageType.Warning : MessageType.Info;
            }
            catch (Exception exception) { SetError(exception); }
        }

        private void DrawScan()
        {
            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Create Recipe (Scan Changes)"), EditorStyles.boldLabel);
            _modifiedAvatar = (GameObject)EditorGUILayout.ObjectField(AvatarRecipeLocalization.Get("Modified Avatar"), _modifiedAvatar,
                typeof(GameObject), true);
            _originalAvatarPrefab = (GameObject)EditorGUILayout.ObjectField(AvatarRecipeLocalization.Get("Original Avatar Prefab"),
                _originalAvatarPrefab, typeof(GameObject), false);

            using (new EditorGUI.DisabledScope(_modifiedAvatar == null || _originalAvatarPrefab == null))
            {
                if (GUILayout.Button(AvatarRecipeLocalization.Get("Start Scan and Create Recipe")))
                {
                    var savedReference = AvatarRecipeProjectSettings.TrackedAvatar;
                    var replacesTracking = AvatarTrackingService.IsTracking ||
                        (savedReference != null && !string.IsNullOrEmpty(savedReference.globalObjectId));
                    if (replacesTracking &&
                        !EditorUtility.DisplayDialog(AvatarRecipeLocalization.Get("Replace Tracking Baseline"),
                            AvatarRecipeLocalization.Get("Scan will replace the active tracking baseline with the selected Original Avatar Prefab. Continue?"),
                            AvatarRecipeLocalization.Get("Scan"), AvatarRecipeLocalization.Get("Cancel")))
                    {
                        return;
                    }

                    try
                    {
                        var state = ExistingAvatarScanner.ScanAndStartTracking(_modifiedAvatar, _originalAvatarPrefab);
                        _statusType = state.manualReview.Count == 0 ? MessageType.Info : MessageType.Warning;
                        _statusMessage = AvatarRecipeLocalization.Format("Scan initialized with {0} added Prefab(s), {1} Transform change(s), {2} BlendShape change(s), {3} Active State change(s), and {4} Material change(s). Save the scene to write the Recipe.",
                            state.addedPrefabs.Count, state.transformChanges.Count, state.blendShapeChanges.Count, state.activeStateChanges.Count, state.materialChanges.Count);
                        if (state.manualReview.Count > 0)
                        {
                            _statusMessage += "\n" + AvatarRecipeLocalization.Get("Manual Review") + ":\n- " + string.Join("\n- ", state.manualReview);
                        }
                    }
                    catch (Exception exception) { SetError(exception); }
                }
            }

            EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("Compare the modified Avatar with its original Prefab to create a Recipe from the differences, then continue tracking. Save the scene to write the Recipe."),
                MessageType.None);
        }

        private void DrawRecipeRoot()
        {
            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Recipe Root"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var recipeRoot = EditorGUILayout.DelayedTextField(AvatarRecipeProjectSettings.RecipeRoot);
                if (EditorGUI.EndChangeCheck()) SetRecipeRoot(recipeRoot);
                if (GUILayout.Button(AvatarRecipeLocalization.Get("Browse"), GUILayout.Width(72))) ChooseRecipeRoot();
            }

            if (string.IsNullOrEmpty(AvatarRecipeProjectSettings.RecipeRoot))
            {
                EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("Choose a Recipe Root to save project and Recipe files."), MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Project Folder"), AvatarRecipeProjectSettings.GetRecipeProjectDirectory());
            if (!Directory.Exists(AvatarRecipeProjectSettings.RecipeRoot))
            {
                EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("Recipe Root was not found. Choose an available folder."), MessageType.Error);
            }
            else if (GUILayout.Button(AvatarRecipeLocalization.Get("Create project.json")))
            {
                try { _statusMessage = AvatarRecipeProjectSettings.CreateProjectIndex(); _statusType = MessageType.Info; }
                catch (Exception exception) { SetError(exception); }
            }
        }

        private void DrawTracking()
        {
            EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Create Recipe (Track Avatar)"), EditorStyles.boldLabel);
            var trackedReference = AvatarRecipeProjectSettings.TrackedAvatar;
            if (AvatarTrackingService.IsTracking)
            {
                EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Avatar Root"), AvatarTrackingService.Root.name);
                EditorGUILayout.LabelField(AvatarRecipeLocalization.Get("Scene"), AvatarTrackingService.Root.scene.path);
                if (!AvatarTrackingService.HasKnownBaseAvatar)
                {
                    EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("The source Prefab could not be identified automatically. Tracking will continue with the Avatar name only."), MessageType.Warning);
                }
                if (!string.IsNullOrEmpty(AvatarTrackingService.Error))
                {
                    EditorGUILayout.HelpBox(AvatarRecipeLocalization.Translate(AvatarTrackingService.Error), MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox(AvatarTrackingService.IsDirty
                        ? AvatarRecipeLocalization.Get("Changes detected. Save the scene (Ctrl+S / Cmd+S) to update the Recipe.")
                        : AvatarRecipeLocalization.Get("Tracking is active. Recipe files update when this scene is saved."), MessageType.Info);
                }

                if (GUILayout.Button(AvatarRecipeLocalization.Get("Stop Tracking")))
                {
                    try { AvatarTrackingService.StopTracking(); _statusMessage = AvatarRecipeLocalization.Get("Tracking stopped."); _statusType = MessageType.Info; }
                    catch (Exception exception) { SetError(exception); }
                }
            }
            else
            {
                _selectedRoot = (GameObject)EditorGUILayout.ObjectField(AvatarRecipeLocalization.Get("Avatar Root"), _selectedRoot, typeof(GameObject), true);
                if (trackedReference != null && !string.IsNullOrEmpty(trackedReference.globalObjectId))
                {
                    EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("Saved tracking reference is not currently active. Select the Avatar Root to resume."),
                        string.IsNullOrEmpty(AvatarTrackingService.Error) ? MessageType.Warning : MessageType.Error);
                }

                using (new EditorGUI.DisabledScope(_selectedRoot == null))
                {
                    if (GUILayout.Button(AvatarRecipeLocalization.Get("Start Tracking")))
                    {
                        try
                        {
                            AvatarTrackingService.StartTracking(_selectedRoot);
                            _statusMessage = AvatarRecipeLocalization.Get("Tracking started. The current Avatar state was saved as a local baseline.");
                            _statusType = MessageType.Info;
                        }
                        catch (Exception exception) { SetError(exception); }
                    }
                }

                if (trackedReference != null && !string.IsNullOrEmpty(trackedReference.globalObjectId))
                {
                    if (GUILayout.Button(AvatarRecipeLocalization.Get("Rebuild Local Baseline…")))
                    {
                        if (EditorUtility.DisplayDialog(AvatarRecipeLocalization.Get("Rebuild Local Baseline"),
                            AvatarRecipeLocalization.Get("The current Avatar state will become the new baseline. Existing Recipe output will only change on the next scene save. Continue?"),
                            AvatarRecipeLocalization.Get("Rebuild"), AvatarRecipeLocalization.Get("Cancel")))
                        {
                            try { AvatarTrackingService.RebuildBaseline(); _statusMessage = AvatarRecipeLocalization.Get("Local baseline rebuilt."); _statusType = MessageType.Info; }
                            catch (Exception exception) { SetError(exception); }
                        }
                    }

                    if (GUILayout.Button(AvatarRecipeLocalization.Get("Clear Saved Tracking Reference")))
                    {
                        try { AvatarTrackingService.StopTracking(); _statusMessage = AvatarRecipeLocalization.Get("Saved tracking reference cleared."); _statusType = MessageType.Info; }
                        catch (Exception exception) { SetError(exception); }
                    }
                }
            }

            EditorGUILayout.HelpBox(AvatarRecipeLocalization.Get("Start tracking an Avatar Root to record supported changes from its current state. Save the scene to create or update the Recipe files."), MessageType.None);
        }

        private void ChooseRecipeRoot()
        {
            var initialPath = AvatarRecipeProjectSettings.RecipeRoot;
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
                initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var selectedPath = EditorUtility.OpenFolderPanel(AvatarRecipeLocalization.Get("Choose Recipe Root"), initialPath, string.Empty);
            if (!string.IsNullOrEmpty(selectedPath)) SetRecipeRoot(selectedPath);
        }

        private void SetRecipeRoot(string path)
        {
            try
            {
                AvatarRecipeProjectSettings.SetRecipeRoot(path);
                _statusMessage = AvatarRecipeLocalization.Get("Recipe Root saved.");
                _statusType = MessageType.Info;
            }
            catch (Exception exception) { SetError(exception); }
        }

        private void SetError(Exception exception)
        {
            _statusMessage = AvatarRecipeLocalization.Translate(exception.Message);
            _statusType = MessageType.Error;
        }
    }
}
