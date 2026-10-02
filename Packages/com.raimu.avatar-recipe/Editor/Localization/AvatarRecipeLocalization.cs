using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarRecipe.Editor.Localization
{
    internal static class AvatarRecipeLocalization
    {
        private const string PreferenceKey = "com.raimu.avatar-recipe.language";
        private static readonly Dictionary<string, string[]> Entries = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Language"] = new[] { "Language", "言語", "语言", "언어" },
            ["Import Recipe"] = new[] { "Import Recipe", "レシピをインポート", "导入Recipe", "Recipe 가져오기" },
            ["Select a target Avatar and a Recipe state.json. Review the compatibility and planned changes before applying; building this preview does not modify the scene."] = new[]
            {
                "Select a target Avatar and a Recipe state.json. Review the compatibility and planned changes before applying; building this preview does not modify the scene.",
                "適用先アバターとRecipeのstate.jsonを指定し、対象と変更内容を確認してから適用します。確認段階ではシーンを変更しません。",
                "选择目标Avatar和Recipe的state.json。应用前请检查兼容情况和计划更改；生成预览不会修改场景。",
                "대상 아바타와 Recipe state.json을 지정하고 호환성 및 변경 계획을 확인한 뒤 적용하세요. 미리보기 단계에서는 씬을 변경하지 않습니다."
            },
            ["Review Apply Plan"] = new[] { "Review Apply Plan", "適用内容を確認", "检查应用内容", "적용 내용 확인" },
            ["Apply Plan"] = new[] { "Apply Plan", "適用予定の内容", "计划应用内容", "적용 계획" },
            ["Create Recipe (Track Avatar)"] = new[] { "Create Recipe (Track Avatar)", "レシピを作成（アバターを追跡）", "创建Recipe（跟踪Avatar）", "Recipe 만들기(아바타 추적)" },
            ["Start tracking an Avatar Root to record supported changes from its current state. Save the scene to create or update the Recipe files."] = new[]
            {
                "Start tracking an Avatar Root to record supported changes from its current state. Save the scene to create or update the Recipe files.",
                "アバタールートを選んで追跡を開始すると、現在の状態を基準に対応する変更を記録します。シーンを保存するとRecipeファイルを作成・更新します。",
                "选择Avatar根对象并开始跟踪，以当前状态为基准记录受支持的更改。保存场景后会创建或更新Recipe文件。",
                "아바타 루트를 선택해 추적을 시작하면 현재 상태를 기준으로 지원되는 변경 사항을 기록합니다. 씬을 저장하면 Recipe 파일을 만들거나 업데이트합니다."
            },
            ["Create Recipe (Scan Changes)"] = new[] { "Create Recipe (Scan Changes)", "レシピを作成（変更をスキャン）", "创建Recipe（扫描更改）", "Recipe 만들기(변경 사항 스캔)" },
            ["Compare the modified Avatar with its original Prefab to create a Recipe from the differences, then continue tracking. Save the scene to write the Recipe."] = new[]
            {
                "Compare the modified Avatar with its original Prefab to create a Recipe from the differences, then continue tracking. Save the scene to write the Recipe.",
                "変更済みアバターを元Prefabと比較して差分からRecipeを作成し、そのまま追跡を開始します。シーンを保存するとRecipeを書き出します。",
                "将已修改的Avatar与原始预制件比较，根据差异创建Recipe，然后继续跟踪。保存场景后写入Recipe。",
                "수정된 아바타를 원본 프리팹과 비교해 차이점으로 Recipe를 만든 뒤 계속 추적합니다. 씬을 저장하면 Recipe를 기록합니다."
            },
            ["Start Scan and Create Recipe"] = new[] { "Start Scan and Create Recipe", "スキャンしてレシピ作成を開始", "开始扫描并创建Recipe", "스캔 후 Recipe 만들기 시작" },
            ["Recipe state.json path is required."] = new[] { "Recipe state.json path is required.", "Recipe state.jsonのパスを指定してください。", "必须指定Recipe state.json路径。", "Recipe state.json 경로를 입력하세요." },
            ["Recipe state.json was not found."] = new[] { "Recipe state.json was not found.", "Recipe state.jsonが見つかりません。", "找不到Recipe state.json。", "Recipe state.json을 찾을 수 없습니다." },
            ["Recipe state.json is invalid: {0}"] = new[] { "Recipe state.json is invalid: {0}", "Recipe state.jsonが不正です: {0}", "Recipe state.json无效：{0}", "Recipe state.json이 올바르지 않습니다: {0}" },
            ["Recipe is missing baseAvatar metadata."] = new[] { "Recipe is missing baseAvatar metadata.", "RecipeにbaseAvatar情報がありません。", "Recipe缺少baseAvatar元数据。", "Recipe에 baseAvatar 메타데이터가 없습니다." },
            ["Modified Avatar must belong to a saved, loaded scene. Save the scene first."] = new[] { "Modified Avatar must belong to a saved, loaded scene. Save the scene first.", "変更済みアバターは保存済みの読み込みシーンに置いてください。先にシーンを保存してください。", "已修改的Avatar必须位于已保存且已加载的场景中。请先保存场景。", "수정된 아바타는 저장된 로드 씬에 있어야 합니다. 먼저 씬을 저장하세요." },
            ["Original Avatar must be the root Prefab asset, not a scene object or nested Prefab object."] = new[] { "Original Avatar must be the root Prefab asset, not a scene object or nested Prefab object.", "元アバターにはシーン上のオブジェクトや入れ子Prefabではなく、ルートPrefabアセットを指定してください。", "原始Avatar必须是根预制件资源，不能是场景对象或嵌套预制件对象。", "원본 아바타에는 씬 오브젝트나 중첩 프리팹이 아닌 루트 프리팹 에셋을 지정하세요." },
            ["Unity could not identify the Original Avatar Prefab asset."] = new[] { "Unity could not identify the Original Avatar Prefab asset.", "元アバターPrefabアセットを特定できませんでした。", "Unity无法识别原始Avatar预制件资源。", "Unity가 원본 아바타 프리팹 에셋을 식별하지 못했습니다." },
            ["Unity could not load the Original Avatar Prefab contents: {0}"] = new[] { "Unity could not load the Original Avatar Prefab contents: {0}", "元アバターPrefabの内容を読み込めませんでした: {0}", "Unity无法加载原始Avatar预制件内容：{0}", "Unity가 원본 아바타 프리팹 내용을 불러오지 못했습니다: {0}" },
            ["Unity could not create a persistent identity for this Avatar Root. Save the scene and try again."] = new[] { "Unity could not create a persistent identity for this Avatar Root. Save the scene and try again.", "このアバタールートの永続IDを作成できませんでした。シーンを保存してからやり直してください。", "Unity无法为此Avatar根对象创建持久标识。请保存场景后重试。", "Unity가 이 아바타 루트의 영구 ID를 만들지 못했습니다. 씬을 저장한 뒤 다시 시도하세요." },
            ["Recipe Root is unavailable: {0}"] = new[] { "Recipe Root is unavailable: {0}", "Recipe保存先を利用できません: {0}", "Recipe根目录不可用：{0}", "Recipe 루트를 사용할 수 없습니다: {0}" },
            ["Choose the UI language. Auto follows the system language; your choice is saved for this Editor user."] = new[]
            {
                "Choose the UI language. Auto follows the system language; your choice is saved for this Editor user.", "表示言語を選択します。「自動」はシステム言語に従います。選択内容はこのEditorユーザー設定に保存されます。",
                "选择界面语言。“自动”将跟随系统语言。所选语言会保存在当前Editor用户设置中。", "UI 언어를 선택합니다. ‘자동’은 시스템 언어를 따릅니다. 선택한 언어는 현재 Editor 사용자 설정에 저장됩니다."
            },
            ["Select Recipe state.json"] = new[] { "Select Recipe state.json", "Recipe state.jsonを選択", "选择Recipe state.json", "Recipe state.json 선택" },
            ["Scan will replace the active tracking baseline with the selected Original Avatar Prefab. Continue?"] = new[]
            {
                "Scan will replace the active tracking baseline with the selected Original Avatar Prefab. Continue?", "スキャンすると、選択した元アバターPrefabで現在の追跡基準を置き換えます。続行しますか？",
                "扫描将使用所选原始Avatar预制件替换当前跟踪基线。是否继续？", "스캔하면 선택한 원본 아바타 프리팹으로 현재 추적 기준을 교체합니다. 계속할까요?"
            },
            ["Scan"] = new[] { "Scan", "スキャン", "扫描", "스캔" },
            ["{0} (sibling {1})"] = new[] { "{0} (sibling {1})", "{0}（同階層位置 {1}）", "{0}（同级位置{1}）", "{0}(형제 순서 {1})" },
            ["{0} — Recipe value"] = new[] { "{0} — Recipe value", "{0} — Recipeの値", "{0} — Recipe值", "{0} — Recipe 값" },
            ["{0} → {1}"] = new[] { "{0} → {1}", "{0} → {1}", "{0} → {1}", "{0} → {1}" },
            ["Removed base object requires manual review: {0}"] = new[]
            {
                "Removed base object requires manual review: {0}", "ベースオブジェクトの削除は手動確認が必要です: {0}", "删除基础对象需要手动检查：{0}", "베이스 오브젝트 삭제는 수동 검토가 필요합니다: {0}"
            },
            ["Removed BlendShape requires manual review: {0}"] = new[]
            {
                "Removed BlendShape requires manual review: {0}", "BlendShapeの削除は手動確認が必要です: {0}", "删除BlendShape需要手动检查：{0}", "BlendShape 삭제는 수동 검토가 필요합니다: {0}"
            },
            ["Auto (System)"] = new[] { "Auto (System)", "自動（システム設定）", "自动（系统设置）", "자동(시스템 설정)" },
            ["Japanese"] = new[] { "Japanese", "日本語", "日语", "일본어" },
            ["English"] = new[] { "English", "英語", "英语", "영어" },
            ["Chinese (Simplified)"] = new[] { "Chinese (Simplified)", "中国語（簡体字）", "简体中文", "중국어(간체)" },
            ["Korean"] = new[] { "Korean", "韓国語", "韩语", "한국어" },
            ["Project Settings"] = new[] { "Project Settings", "プロジェクト設定", "项目设置", "프로젝트 설정" },
            ["Project ID"] = new[] { "Project ID", "プロジェクトID", "项目ID", "프로젝트 ID" },
            ["Unity Project"] = new[] { "Unity Project", "Unityプロジェクト", "Unity项目", "Unity 프로젝트" },
            ["Recipe Root"] = new[] { "Recipe Root", "Recipe保存先", "Recipe根目录", "Recipe 루트" },
            ["Browse"] = new[] { "Browse", "参照", "浏览", "찾아보기" },
            ["Project Folder"] = new[] { "Project Folder", "プロジェクトフォルダー", "项目文件夹", "프로젝트 폴더" },
            ["Create project.json"] = new[] { "Create project.json", "project.jsonを作成", "创建project.json", "project.json 만들기" },
            ["Choose a Recipe Root to save project and Recipe files."] = new[]
            {
                "Choose a Recipe Root to save project and Recipe files.", "プロジェクトとRecipeファイルの保存先を選択してください。",
                "请选择用于保存项目和Recipe文件的根目录。", "프로젝트와 Recipe 파일을 저장할 루트를 선택하세요."
            },
            ["Recipe Root was not found. Choose an available folder."] = new[]
            {
                "Recipe Root was not found. Choose an available folder.", "Recipe保存先が見つかりません。利用可能なフォルダーを選択してください。",
                "找不到Recipe根目录。请选择可用文件夹。", "Recipe 루트를 찾을 수 없습니다. 사용 가능한 폴더를 선택하세요."
            },
            ["Tracking"] = new[] { "Tracking", "トラッキング", "跟踪", "추적" },
            ["Avatar Root"] = new[] { "Avatar Root", "アバタールート", "Avatar根对象", "아바타 루트" },
            ["Scene"] = new[] { "Scene", "シーン", "场景", "씬" },
            ["Stop Tracking"] = new[] { "Stop Tracking", "追跡を停止", "停止跟踪", "추적 중지" },
            ["Start Tracking"] = new[] { "Start Tracking", "追跡を開始", "开始跟踪", "추적 시작" },
            ["Rebuild Local Baseline…"] = new[] { "Rebuild Local Baseline…", "ローカル基準を再構築…", "重建本地基线…", "로컬 기준 다시 만들기…" },
            ["Clear Saved Tracking Reference"] = new[] { "Clear Saved Tracking Reference", "保存済みの追跡参照を消去", "清除已保存的跟踪引用", "저장된 추적 참조 지우기" },
            ["Scan Existing Avatar"] = new[] { "Scan Existing Avatar", "既存アバターをスキャン", "扫描现有Avatar", "기존 아바타 스캔" },
            ["Modified Avatar"] = new[] { "Modified Avatar", "変更済みアバター", "已修改的Avatar", "수정된 아바타" },
            ["Original Avatar Prefab"] = new[] { "Original Avatar Prefab", "元アバターPrefab", "原始Avatar预制件", "원본 아바타 프리팹" },
            ["Scan and Start Tracking"] = new[] { "Scan and Start Tracking", "スキャンして追跡を開始", "扫描并开始跟踪", "스캔 후 추적 시작" },
            ["Scan compares an existing modified Avatar with its original Prefab, then starts normal Tracking. Recipe output is written when the modified scene is saved."] = new[]
            {
                "Scan compares an existing modified Avatar with its original Prefab, then starts normal Tracking. Recipe output is written when the modified scene is saved.",
                "変更済みアバターを元Prefabと比較してから通常の追跡を開始します。Recipeは変更済みシーンの保存時に書き出されます。",
                "扫描会将已修改的Avatar与原始预制件比较，然后开始常规跟踪。保存修改后的场景时才会写入Recipe。",
                "수정된 아바타를 원본 프리팹과 비교한 뒤 일반 추적을 시작합니다. 수정된 씬을 저장할 때 Recipe가 기록됩니다."
            },
            ["Compatibility / Preview"] = new[] { "Compatibility / Preview", "互換性 / プレビュー", "兼容性 / 预览", "호환성 / 미리보기" },
            ["Target Avatar"] = new[] { "Target Avatar", "適用先アバター", "目标Avatar", "대상 아바타" },
            ["Recipe state.json"] = new[] { "Recipe state.json", "Recipe state.json", "Recipe state.json", "Recipe state.json" },
            ["Build Compatibility Preview"] = new[] { "Build Compatibility Preview", "互換性プレビューを作成", "生成兼容性预览", "호환성 미리보기 만들기" },
            ["Planned Operations"] = new[] { "Planned Operations", "予定される操作", "计划操作", "예정 작업" },
            ["No supported operations."] = new[] { "No supported operations.", "適用可能な操作はありません。", "没有可应用的操作。", "적용할 수 있는 작업이 없습니다." },
            ["Apply Previewed Recipe"] = new[] { "Apply Previewed Recipe", "プレビューしたRecipeを適用", "应用预览的Recipe", "미리 본 Recipe 적용" },
            ["Preview created. The target scene was not modified."] = new[]
            {
                "Preview created. The target scene was not modified.", "プレビューを作成しました。適用先シーンは変更されていません。",
                "已生成预览。目标场景未被修改。", "미리보기를 만들었습니다. 대상 씬은 변경되지 않았습니다."
            },
            ["Compatibility check complete. All reported targets are resolvable; no scene changes were made."] = new[]
            {
                "Compatibility check complete. All reported targets are resolvable; no scene changes were made.", "互換性チェックが完了しました。対象はすべて解決でき、シーンは変更されていません。",
                "兼容性检查完成。所有目标均可解析，场景未被修改。", "호환성 확인이 끝났습니다. 모든 대상을 찾았으며 씬은 변경되지 않았습니다."
            },
            ["Compatibility check complete. Review missing, ambiguous, conflicting, and manual review items before applying."] = new[]
            {
                "Compatibility check complete. Review missing, ambiguous, conflicting, and manual review items before applying.", "互換性チェックが完了しました。未検出・曖昧・競合・手動確認項目を確認してから適用してください。",
                "兼容性检查完成。应用前请检查缺失、歧义、冲突和需手动确认的项目。", "호환성 확인이 끝났습니다. 적용 전에 누락, 모호성, 충돌 및 수동 검토 항목을 확인하세요."
            },
            ["Apply is blocked. Resolve all missing or ambiguous items, then build a new preview."] = new[]
            {
                "Apply is blocked. Resolve all missing or ambiguous items, then build a new preview.", "適用できません。未検出または曖昧な項目を解決してから、プレビューを作り直してください。",
                "无法应用。请先解决缺失或有歧义的项目，再重新生成预览。", "적용할 수 없습니다. 누락되었거나 모호한 항목을 해결한 뒤 미리보기를 다시 만드세요."
            },
            ["Recipe Conflicts"] = new[] { "Recipe Conflicts", "Recipeの競合", "Recipe冲突", "Recipe 충돌" },
            ["Apply Anyway"] = new[] { "Apply Anyway", "そのまま適用", "仍然应用", "그대로 적용" },
            ["Skip Conflicts"] = new[] { "Skip Conflicts", "競合をスキップ", "跳过冲突", "충돌 건너뛰기" },
            ["Cancel"] = new[] { "Cancel", "キャンセル", "取消", "취소" },
            ["Manual Review Required"] = new[] { "Manual Review Required", "手動確認が必要です", "需要手动检查", "수동 검토 필요" },
            ["Apply Supported"] = new[] { "Apply Supported", "対応項目を適用", "应用支持的项目", "지원 항목 적용" },
            ["Apply Recipe"] = new[] { "Apply Recipe", "Recipeを適用", "应用Recipe", "Recipe 적용" },
            ["Apply"] = new[] { "Apply", "適用", "应用", "적용" },
            ["Replace Tracking Baseline"] = new[] { "Replace Tracking Baseline", "追跡基準を置き換え", "替换跟踪基线", "추적 기준 교체" },
            ["Rebuild Local Baseline"] = new[] { "Rebuild Local Baseline", "ローカル基準を再構築", "重建本地基线", "로컬 기준 다시 만들기" },
            ["Rebuild"] = new[] { "Rebuild", "再構築", "重建", "다시 만들기" },
            ["Choose Recipe Root"] = new[] { "Choose Recipe Root", "Recipe保存先を選択", "选择Recipe根目录", "Recipe 루트 선택" },
            ["Recipe Root saved."] = new[] { "Recipe Root saved.", "Recipe保存先を保存しました。", "Recipe根目录已保存。", "Recipe 루트를 저장했습니다." },
            ["Tracking stopped."] = new[] { "Tracking stopped.", "追跡を停止しました。", "已停止跟踪。", "추적을 중지했습니다." },
            ["Saved tracking reference cleared."] = new[] { "Saved tracking reference cleared.", "保存済みの追跡参照を消去しました。", "已清除已保存的跟踪引用。", "저장된 추적 참조를 지웠습니다." },
            ["Local baseline rebuilt."] = new[] { "Local baseline rebuilt.", "ローカル基準を再構築しました。", "已重建本地基线。", "로컬 기준을 다시 만들었습니다." },
            ["Only the selected Avatar Root subtree is tracked. Recipe files are written on Unity scene save."] = new[]
            {
                "Only the selected Avatar Root subtree is tracked. Recipe files are written on Unity scene save.", "選択したアバタールート以下だけを追跡します。Recipeファイルはシーン保存時に書き出されます。",
                "仅跟踪所选Avatar根对象及其子层级。保存Unity场景时会写入Recipe文件。", "선택한 아바타 루트 하위만 추적합니다. Unity 씬을 저장할 때 Recipe 파일을 기록합니다."
            },
            ["Changes detected. Save the scene (Ctrl+S / Cmd+S) to update the Recipe."] = new[]
            {
                "Changes detected. Save the scene (Ctrl+S / Cmd+S) to update the Recipe.", "変更を検出しました。シーンを保存（Ctrl+S / Cmd+S）するとRecipeが更新されます。",
                "检测到更改。保存场景（Ctrl+S / Cmd+S）以更新Recipe。", "변경 사항을 감지했습니다. 씬을 저장(Ctrl+S / Cmd+S)하면 Recipe가 업데이트됩니다."
            },
            ["Tracking is active. Recipe files update when this scene is saved."] = new[]
            {
                "Tracking is active. Recipe files update when this scene is saved.", "追跡中です。このシーンを保存するとRecipeファイルが更新されます。",
                "正在跟踪。保存此场景时会更新Recipe文件。", "추적 중입니다. 이 씬을 저장하면 Recipe 파일이 업데이트됩니다."
            },
            ["Saved tracking reference is not currently active. Select the Avatar Root to resume."] = new[]
            {
                "Saved tracking reference is not currently active. Select the Avatar Root to resume.", "保存された追跡参照は現在有効ではありません。再開するにはアバタールートを選択してください。",
                "已保存的跟踪引用当前未激活。请选择Avatar根对象以恢复跟踪。", "저장된 추적 참조가 현재 활성화되어 있지 않습니다. 다시 시작하려면 아바타 루트를 선택하세요."
            },
            ["Base Avatar"] = new[] { "Base Avatar", "ベースアバター", "基础Avatar", "베이스 아바타" },
            ["Required Prefab"] = new[] { "Required Prefab", "必要なPrefab", "所需预制件", "필요한 프리팹" },
            ["Prefab Parent"] = new[] { "Prefab Parent", "Prefabの親", "预制件父对象", "프리팹 부모" },
            ["Transform"] = new[] { "Transform", "Transform", "Transform", "Transform" },
            ["Active State"] = new[] { "Active State", "有効状態", "启用状态", "활성 상태" },
            ["BlendShape"] = new[] { "BlendShape", "BlendShape", "BlendShape", "BlendShape" },
            ["BlendShape Renderer"] = new[] { "BlendShape Renderer", "BlendShape Renderer", "BlendShape渲染器", "BlendShape 렌더러" },
            ["Manual Review"] = new[] { "Manual Review", "手動確認", "手动检查", "수동 검토" },
            ["Found"] = new[] { "Found", "検出", "已找到", "찾음" },
            ["Missing"] = new[] { "Missing", "未検出", "缺失", "누락" },
            ["Ambiguous"] = new[] { "Ambiguous", "曖昧", "有歧义", "모호함" },
            ["Conflict"] = new[] { "Conflict", "競合", "冲突", "충돌" },
            ["ManualReview"] = new[] { "Manual Review", "手動確認", "手动检查", "수동 검토" },
            ["Base Avatar matches {0}"] = new[] { "Base Avatar matches {0}", "ベースアバターは{0}と一致します", "基础Avatar与{0}匹配", "베이스 아바타가 {0}와 일치합니다" },
            ["Target Avatar is not connected to a source Prefab; expected {0}"] = new[]
            {
                "Target Avatar is not connected to a source Prefab; expected {0}", "適用先アバターに元Prefabがありません。必要なPrefab: {0}",
                "目标Avatar未关联源预制件；预期为{0}", "대상 아바타가 원본 프리팹과 연결되어 있지 않습니다. 필요한 프리팹: {0}"
            },
            ["Target source Prefab {0} does not match expected {1} ({2})"] = new[]
            {
                "Target source Prefab {0} does not match expected {1} ({2})", "適用先の元Prefab {0} は、必要なPrefab {1} ({2})と一致しません",
                "目标源预制件{0}与预期的{1}（{2}）不匹配", "대상 원본 프리팹 {0}이(가) 필요한 프리팹 {1}({2})과(와) 일치하지 않습니다"
            },
            ["{0} resolves to the Base Avatar Prefab and cannot be treated as an independently added Prefab."] = new[]
            {
                "{0} resolves to the Base Avatar Prefab and cannot be treated as an independently added Prefab.", "{0}はベースアバターPrefab自身を指しているため、追加Prefabとして扱えません。",
                "{0}解析为基础Avatar预制件，不能作为独立添加的预制件处理。", "{0}이(가) 베이스 아바타 프리팹으로 확인되어 별도로 추가된 프리팹으로 처리할 수 없습니다."
            },
            ["{0}: resolved by GUID at {1}"] = new[] { "{0}: resolved by GUID at {1}", "{0}: GUIDから{1}を特定", "{0}：通过GUID定位到{1}", "{0}: GUID로 {1}에서 찾음" },
            ["{0}: resolved by asset path {1}"] = new[] { "{0}: resolved by asset path {1}", "{0}: アセットパス{1}から特定", "{0}：通过资源路径{1}定位", "{0}: 에셋 경로 {1}에서 찾음" },
            ["{0}: resolved by exact name at {1}"] = new[] { "{0}: resolved by exact name at {1}", "{0}: 完全一致する名前から{1}を特定", "{0}：通过完全匹配的名称定位到{1}", "{0}: 정확히 일치하는 이름으로 {1}에서 찾음" },
            ["multiple exact-name candidates: {0}"] = new[] { "multiple exact-name candidates: {0}", "完全一致する候補が複数あります: {0}", "存在多个完全匹配的候选项：{0}", "정확히 일치하는 후보가 여러 개 있습니다: {0}" },
            ["missing; expected {0} (GUID {1})"] = new[] { "missing; expected {0} (GUID {1})", "未検出。必要なパス: {0}（GUID {1}）", "缺失；预期路径{0}（GUID {1}）", "누락됨. 필요한 경로: {0}(GUID {1})" },
            ["Could not resolve parent for {0} at {1}"] = new[] { "Could not resolve parent for {0} at {1}", "{0}の親を{1}で特定できません", "无法在{1}解析{0}的父对象", "{1}에서 {0}의 부모를 찾을 수 없습니다" },
            ["Current value differs from Recipe baseline at {0}"] = new[]
            {
                "Current value differs from Recipe baseline at {0}", "{0}の現在値がRecipeの基準値と異なります", "{0}的当前值与Recipe基线不同", "{0}의 현재 값이 Recipe 기준값과 다릅니다"
            },
            ["Target found at {0}"] = new[] { "Target found at {0}", "対象を検出: {0}", "已找到目标：{0}", "대상 찾음: {0}" },
            ["Ambiguous target: {0}"] = new[] { "Ambiguous target: {0}", "対象が曖昧です: {0}", "目标有歧义：{0}", "대상이 모호합니다: {0}" },
            ["Missing target: {0}"] = new[] { "Missing target: {0}", "対象が見つかりません: {0}", "未找到目标：{0}", "대상을 찾을 수 없습니다: {0}" },
            ["Unsupported Recipe change kind requires manual review: {0}"] = new[]
            {
                "Unsupported Recipe change kind requires manual review: {0}", "未対応のRecipe変更のため手動確認が必要です: {0}",
                "此Recipe更改类型不受支持，需手动检查：{0}", "지원하지 않는 Recipe 변경 유형입니다. 수동 검토가 필요합니다: {0}"
            },
            ["Applied {0} operation(s). {1} conflict(s) skipped; {2} manual review item(s) left unresolved. Save the scene to keep the changes. One Undo reverts the Apply."] = new[]
            {
                "Applied {0} operation(s). {1} conflict(s) skipped; {2} manual review item(s) left unresolved. Save the scene to keep the changes. One Undo reverts the Apply.",
                "{0}件の操作を適用しました。競合を{1}件スキップし、手動確認項目が{2}件残っています。変更を保持するにはシーンを保存してください。Undo一度で適用全体を戻せます。",
                "已应用{0}项操作，跳过{1}项冲突，尚有{2}项需手动检查。保存场景以保留更改。一次撤销即可还原本次应用。",
                "작업 {0}개를 적용했습니다. 충돌 {1}개를 건너뛰었고 수동 검토 항목 {2}개가 남았습니다. 변경 사항을 유지하려면 씬을 저장하세요. Undo 한 번으로 전체 적용을 되돌릴 수 있습니다."
            },
            ["Scan initialized with {0} added Prefab(s), {1} Transform change(s), {2} BlendShape change(s), and {3} Active State change(s). Save the scene to write the Recipe."] = new[]
            {
                "Scan initialized with {0} added Prefab(s), {1} Transform change(s), {2} BlendShape change(s), and {3} Active State change(s). Save the scene to write the Recipe.",
                "スキャンを開始しました。追加Prefab {0}件、Transform変更 {1}件、BlendShape変更 {2}件、Active State変更 {3}件です。シーンを保存するとRecipeが書き出されます。",
                "扫描已初始化：新增预制件{0}个、Transform更改{1}项、BlendShape更改{2}项、启用状态更改{3}项。保存场景以写入Recipe。",
                "스캔을 시작했습니다. 추가 프리팹 {0}개, Transform 변경 {1}개, BlendShape 변경 {2}개, 활성 상태 변경 {3}개입니다. 씬을 저장하면 Recipe가 기록됩니다."
            },
            ["{0} conflict(s) were found. Apply Anyway overwrites conflicting values. Skip Conflicts leaves conflicting values unchanged. Manual Review items are always skipped."] = new[]
            {
                "{0} conflict(s) were found. Apply Anyway overwrites conflicting values. Skip Conflicts leaves conflicting values unchanged. Manual Review items are always skipped.",
                "競合が{0}件あります。「そのまま適用」は競合値を上書きし、「競合をスキップ」は現在値を保持します。手動確認項目は常にスキップされます。",
                "发现{0}项冲突。“仍然应用”会覆盖冲突值；“跳过冲突”会保留当前值。需手动检查的项目始终跳过。",
                "충돌 {0}개를 찾았습니다. '그대로 적용'은 충돌 값을 덮어쓰고, '충돌 건너뛰기'는 현재 값을 유지합니다. 수동 검토 항목은 항상 건너뜁니다."
            },
            ["{0} unsupported or manual review item(s) will be skipped. Apply the supported operations?"] = new[]
            {
                "{0} unsupported or manual review item(s) will be skipped. Apply the supported operations?", "未対応または手動確認項目{0}件はスキップされます。対応する操作を適用しますか？",
                "将跳过{0}项不支持或需手动检查的项目。是否应用支持的操作？", "지원하지 않거나 수동 검토가 필요한 항목 {0}개를 건너뜁니다. 지원되는 작업을 적용할까요?"
            },
            ["Apply {0} planned operation(s)? One Undo will revert this Apply."] = new[]
            {
                "Apply {0} planned operation(s)? One Undo will revert this Apply.", "予定されている{0}件の操作を適用しますか？Undo一度で戻せます。",
                "要应用计划中的{0}项操作吗？一次撤销即可还原。", "예정된 작업 {0}개를 적용할까요? Undo 한 번으로 되돌릴 수 있습니다."
            },
            ["The current Avatar state will become the new baseline. Existing Recipe output will only change on the next scene save. Continue?"] = new[]
            {
                "The current Avatar state will become the new baseline. Existing Recipe output will only change on the next scene save. Continue?",
                "現在のアバター状態を新しい基準にします。既存Recipeは次回のシーン保存時に更新されます。続行しますか？",
                "当前Avatar状态将成为新基线。现有Recipe仅会在下次保存场景时更改。是否继续？",
                "현재 아바타 상태를 새 기준으로 사용합니다. 기존 Recipe는 다음 씬 저장 시에만 변경됩니다. 계속할까요?"
            },
            ["The source Prefab could not be identified automatically. Tracking will continue with the Avatar name only."] = new[]
            {
                "The source Prefab could not be identified automatically. Tracking will continue with the Avatar name only.", "元Prefabを自動判定できませんでした。アバター名のみで追跡を続けます。",
                "无法自动识别源预制件。将仅使用Avatar名称继续跟踪。", "원본 프리팹을 자동으로 식별할 수 없습니다. 아바타 이름만으로 추적을 계속합니다."
            },
            ["Recipe Rootのパスを入力してください。"] = new[] { "Enter a Recipe Root path.", "Recipe Rootのパスを入力してください。", "请输入Recipe根目录路径。", "Recipe 루트 경로를 입력하세요." },
            ["Recipe Rootを先に選択してください。"] = new[] { "Choose a Recipe Root first.", "Recipe Rootを先に選択してください。", "请先选择Recipe根目录。", "먼저 Recipe 루트를 선택하세요." },
            ["Recipe Rootが見つかりません: {0}"] = new[] { "Recipe Root was not found: {0}", "Recipe Rootが見つかりません: {0}", "找不到Recipe根目录：{0}", "Recipe 루트를 찾을 수 없습니다: {0}" },
            ["Existing project.json belongs to another project or is invalid: {0}"] = new[]
            {
                "Existing project.json belongs to another project or is invalid: {0}", "既存のproject.jsonは別プロジェクトのものか、無効です: {0}",
                "现有project.json属于其他项目或无效：{0}", "기존 project.json이 다른 프로젝트에 속하거나 올바르지 않습니다: {0}"
            },
            ["Avatar Root must belong to a saved, loaded scene. Save the scene first."] = new[]
            {
                "Avatar Root must belong to a saved, loaded scene. Save the scene first.", "アバタールートは保存済みで読み込み中のシーンに置いてください。先にシーンを保存してください。",
                "Avatar根对象必须位于已保存且已加载的场景中。请先保存场景。", "아바타 루트는 저장되고 로드된 씬에 있어야 합니다. 먼저 씬을 저장하세요."
            },
            ["Local baseline is missing. Rebuild it explicitly after confirming the current avatar is the intended baseline."] = new[]
            {
                "Local baseline is missing. Rebuild it explicitly after confirming the current avatar is the intended baseline.", "ローカル基準がありません。現在のアバターを基準にしてよいか確認してから、明示的に再構築してください。",
                "本地基线缺失。请确认当前Avatar确实应作为基线后，再明确重建基线。", "로컬 기준이 없습니다. 현재 아바타를 기준으로 사용할지 확인한 뒤 명시적으로 다시 만드세요."
            },
            ["Target Avatar must belong to a loaded scene."] = new[]
            {
                "Target Avatar must belong to a loaded scene.", "適用先アバターは読み込み済みシーンに置いてください。", "目标Avatar必须位于已加载的场景中。", "대상 아바타는 로드된 씬에 있어야 합니다."
            },
            ["Apply is blocked while targets or Prefabs are missing or ambiguous. Resolve them and build a new preview."] = new[]
            {
                "Apply is blocked while targets or Prefabs are missing or ambiguous. Resolve them and build a new preview.", "対象やPrefabが未検出または曖昧な間は適用できません。解決してプレビューを作り直してください。",
                "目标或预制件缺失或有歧义时无法应用。请解决问题并重新生成预览。", "대상이나 프리팹을 찾지 못했거나 모호하면 적용할 수 없습니다. 문제를 해결하고 미리보기를 다시 만드세요."
            },
            ["Unsupported Recipe schema version {0}. This package supports version {1}; update Avatar Recipe or migrate the Recipe explicitly."] = new[]
            {
                "Unsupported Recipe schema version {0}. This package supports version {1}; update Avatar Recipe or migrate the Recipe explicitly.", "Recipe schema version {0}には対応していません。このパッケージはversion {1}に対応します。Avatar Recipeを更新するか、Recipeを明示的に移行してください。",
                "不支持Recipe schema版本{0}。此软件包支持版本{1}；请更新Avatar Recipe或明确迁移Recipe。", "지원하지 않는 Recipe 스키마 버전 {0}입니다. 이 패키지는 {1} 버전을 지원합니다. Avatar Recipe를 업데이트하거나 Recipe를 명시적으로 마이그레이션하세요."
            },
            ["Tracking started. The current Avatar state was saved as a local baseline."] = new[]
            {
                "Tracking started. The current Avatar state was saved as a local baseline.", "追跡を開始し、現在のアバター状態をローカル基準として保存しました。",
                "已开始跟踪，并将当前Avatar状态保存为本地基线。", "추적을 시작했고 현재 아바타 상태를 로컬 기준으로 저장했습니다."
            }
        };

        public static string Get(string key)
        {
            if (!Entries.TryGetValue(key, out var translations)) return key;
            var index = CurrentLanguageIndex;
            return index >= 0 && index < translations.Length ? translations[index] : key;
        }

        public static string Format(string key, params object[] arguments) =>
            string.Format(CultureInfo.InvariantCulture, Get(key), arguments);

        public static string Translate(string message)
        {
            if (message == null) return string.Empty;
            if (Entries.ContainsKey(message)) return Get(message);

            foreach (var entry in Entries)
            {
                var argumentIndexes = new List<int>();
                var pattern = new StringBuilder("^");
                var cursor = 0;
                foreach (Match placeholder in Regex.Matches(entry.Key, @"\{(\d+)\}"))
                {
                    pattern.Append(Regex.Escape(entry.Key.Substring(cursor, placeholder.Index - cursor)));
                    pattern.Append("([\\s\\S]*?)");
                    argumentIndexes.Add(int.Parse(placeholder.Groups[1].Value, CultureInfo.InvariantCulture));
                    cursor = placeholder.Index + placeholder.Length;
                }
                if (argumentIndexes.Count == 0) continue;
                pattern.Append(Regex.Escape(entry.Key.Substring(cursor))).Append("$");
                var match = Regex.Match(message, pattern.ToString(), RegexOptions.CultureInvariant);
                if (!match.Success) continue;

                var arguments = new object[argumentIndexes.Count];
                for (var i = 0; i < argumentIndexes.Count; i++) arguments[argumentIndexes[i]] = match.Groups[i + 1].Value;
                return Format(entry.Key, arguments);
            }
            return message;
        }

        public static int LanguageChoice => EditorPrefs.GetInt(PreferenceKey, 0);

        public static string[] LanguageOptions => new[]
        {
            Get("Auto (System)"), Get("Japanese"), Get("English"),
            Get("Chinese (Simplified)"), Get("Korean")
        };

        public static void SetLanguageChoice(int choice) => EditorPrefs.SetInt(PreferenceKey, Mathf.Clamp(choice, 0, 4));

        private static int CurrentLanguageIndex
        {
            get
            {
                switch (LanguageChoice)
                {
                    case 1: return 1;
                    case 2: return 0;
                    case 3: return 2;
                    case 4: return 3;
                    default:
                        var system = Application.systemLanguage.ToString();
                        if (system.IndexOf("Japanese", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                        if (system.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
                        if (system.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
                        return 0;
                }
            }
        }

    }
}
