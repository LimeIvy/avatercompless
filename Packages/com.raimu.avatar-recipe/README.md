# Avatar Recipe

Avatar Recipe は、Unity Editor 上でアバターの対応済み変更を記録し、別プロジェクトのアバターに適用する Editor 専用 VPM / UPM パッケージです。

## 動作環境

- Unity 2022.3 以降
- Editor 専用。ビルド後のアプリにはコードを含めません。
- MVP は VRChat SDK に依存しません。

## 開発プロジェクトへの追加

このリポジトリを Unity プロジェクトとして開くと、`Packages/com.raimu.avatar-recipe` の埋め込みパッケージとして読み込まれます。別プロジェクトで開発版を試す場合は、Package Manager の **Add package from disk...** からこのフォルダーの `package.json` を指定してください。

公開 VPM リポジトリは設定されていません。配布する場合は、ホストした VPM リポジトリにこのパッケージを登録してから、そのリポジトリ URL を利用者に案内してください。

## 基本的な使い方

1. **Window > Avatar Recipe** を開きます。
2. Recipe Root を設定します。
3. **新しいレシピを作る:** Avatar Root を選んで追跡を開始するか、変更済みアバターと元 Prefab を指定して差分をスキャンします。
4. アバターを編集し、シーンを保存します。Recipe ファイルは保存時に更新されます。
5. 適用先プロジェクトでベースアバターと必要な Prefab を先にインポートします。
6. `state.json` を選び、Compatibility Preview の結果を確認してから Apply します。
7. 一度の Undo で Apply 全体を戻せます。

Recipe には差分とアセット参照のみを保存します。元アバターや Prefab のデータはコピーしません。シーン構造の追加・削除、未対応項目は Manual Review に表示されます。

追跡では開始時のアバター状態を基準に、その後の対応済み変更を記録します。スキャンでは変更済みアバターを元Prefabと比較してレシピを作り、そのまま追跡を始めます。どちらの方法も、シーンを保存するとRecipeが作成・更新されます。既存レシピを使うときは **Import Recipe** で対象アバターと `state.json` を選び、適用内容を確認してから適用します。

## 表示言語

ウィンドウ上部の **Language** から日本語、英語、簡体字中国語、韓国語を選べます。初期設定の **Auto (System)** はUnity Editorのシステム言語に従います。選択した言語はEditorユーザー設定に保存され、Recipeファイルには影響しません。

### English

Avatar Recipe is an Editor-only Unity package for creating and importing avatar change Recipes. To create one, track an Avatar Root to record supported changes from its current state, or scan a modified Avatar against its original Prefab. Save the scene to write the Recipe. To use an existing Recipe, open **Import Recipe**, choose the target Avatar and `state.json`, review the apply plan, then apply it. The UI supports Japanese, English, Simplified Chinese, and Korean; **Auto (System)** follows the Editor's system language.

### 简体中文

Avatar Recipe 是一个仅在 Unity Editor 中运行的工具，用于创建和导入 Avatar 修改Recipe。创建Recipe时，可以跟踪Avatar根对象并记录之后的受支持更改，也可以扫描已修改的Avatar并与原始预制件比较。保存场景后写入Recipe。使用已有Recipe时，打开 **Import Recipe**，选择目标Avatar和`state.json`，检查应用计划后再应用。界面支持日语、英语、简体中文和韩语；**Auto (System)** 会跟随Editor的系统语言。

### 한국어

Avatar Recipe는 Unity Editor 전용 도구로, 아바타 변경 Recipe를 만들고 가져옵니다. Recipe를 만들 때 아바타 루트를 추적해 지원되는 변경 사항을 기록하거나, 수정된 아바타를 원본 프리팹과 비교해 스캔할 수 있습니다. 씬을 저장하면 Recipe가 기록됩니다. 기존 Recipe를 사용하려면 **Import Recipe**에서 대상 아바타와 `state.json`을 선택하고 적용 계획을 확인한 뒤 적용하세요. UI는 일본어, 영어, 중국어 간체, 한국어를 지원하며 **Auto (System)**은 Editor 시스템 언어를 따릅니다.

## Recipe schema

現在の `state.json` は schema version 1 です。異なる schema version は自動変換せず、対応バージョンを明示したエラーとして扱います。移行方針はリポジトリの `docs/RECIPE_FORMAT.md` を参照してください。

## 開発ドキュメント

リポジトリルートの `docs/` に仕様、テスト手順、ロードマップがあります。
