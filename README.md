# RO3 Asia 日本語化パッチ

RO3 Asia（PC版）を日本語で遊ぶための日本語化パッチです。

翻訳データ、XUnity AutoTranslator向け辞書、補助プラグイン、インストーラー、更新機能をこのリポジトリで管理しています。

## ダウンロード

最新版は GitHub Releases からダウンロードできます。

- **最新リリース**: [GitHub Releases](https://github.com/MiKoP4/RO3_Asia_Japanese_Patch/releases/latest)

通常の利用では、リポジトリをCloneする必要はありません。ReleaseのZIPを利用してください。

## インストール方法

1. RO3を終了します。
2. 最新Releaseの `RO3_Asia_Japanese_Patch_*.zip` を任意のフォルダへ展開します。
3. `Install-Japanese.bat` を実行します。
4. `ro3.exe` があるRO3の `Client` フォルダを指定します。
   - `ro3.exe` または `Client` フォルダを `Install-Japanese.bat` へドラッグ＆ドロップして指定することもできます。
5. `[OK] Japanese patch installed` と表示されたら、通常どおりRO3を起動します。

以前のバージョンが導入済みの場合も、同じ手順で上書きインストールできます。

## 更新方法

導入済みの日本語化パッチは、同梱の `Update-Japanese.bat` から更新できます。

1. RO3を終了します。
2. `Update-Japanese.bat` を実行します。
3. `ro3.exe` があるRO3の `Client` フォルダを指定します。
4. GitHub Releasesから最新版を確認し、必要な場合は最新ZIPをダウンロードして自動更新します。

更新時は、GitHub側でSHA-256が提供されている場合にダウンロードしたZIPの整合性も確認します。

古いReleaseから展開した `Update-Japanese.bat` でも、GitHub上の最新Releaseを確認して更新できます。

## Releaseに含まれるもの

- 日本語翻訳辞書
- `RO3.LocalizationTablePatcher.dll`
- BepInExランタイム
- XUnity AutoTranslator / ResourceRedirector
- 日本語表示用TextMeshProフォールバックフォントアセット
- インストーラー / アップデーター / アンインストーラー

## 翻訳を修正する場合

翻訳の正本は `_TranslationWorkspace/split_1000/part_*.tsv` です。

生成済みの `Client/BepInEx/Translation/ja/Text/*.txt` を直接編集せず、必ずTSV側を修正してください。

### 1. 翻訳を編集

```text
_TranslationWorkspace/split_1000/part_*.tsv
```

### 2. 翻訳データを検証

```bat
Validate-Translations.bat
```

### 3. 辞書を生成してゲームへ反映

```bat
Build-And-Deploy.bat
```

辞書生成だけを行う場合は `Import-Translations.bat` を使用します。

このリポジトリがRO3のゲームフォルダ直下にある場合、`Build-And-Deploy.bat` は親フォルダの `Client` へ生成物を反映します。

## Releaseの作成

### 自動Release

`main` ブランチへpushすると、GitHub Actionsの `Release on main` workflowが自動実行されます。

自動Releaseでは次の処理を行います。

1. 翻訳正本を検証
2. 直前の最新ReleaseからBepInEx / XUnityのランタイムを取得して整合性を確認
3. 現在の `main` にある翻訳、設定、補助プラグイン、インストーラーを反映
4. 配布用ZIPとSHA-256ファイルを生成
5. `vYYYY.MM.DD.N` 形式のGitHub Releaseを公開

補助プラグインのソースを変更したのに、追跡対象の `RO3.LocalizationTablePatcher.dll` が更新されていない場合は、Releaseを失敗させるチェックも入っています。

### 手動Release

ローカルのゲーム環境を利用して手動でRelease ZIPを作る場合は、次を実行します。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Release.ps1 -Version <バージョン名>
```

生成物は `dist/` に出力されます。

## ディレクトリ構成

- `_TranslationWorkspace/` : 翻訳正本TSVと辞書生成・検証スクリプト
- `Client/` : Releaseへ含める翻訳辞書、設定、補助プラグイン、フォントアセット
- `src/` : `RO3.LocalizationTablePatcher` のソース
- `scripts/` : ビルド、配布、CI向けスクリプト
- `packaging/` : インストーラー、アップデーター、アンインストーラー
- `third_party/` : 第三者コンポーネントのライセンスとビルド用参照
- `.github/workflows/` : GitHub Actions workflow

## 技術的な補足

- XUnity AutoTranslatorだけでは処理されないモンスター/NPC頭上名や一部のワールド空間UIについては、`RO3.LocalizationTablePatcher.dll` がゲーム側のLocalizationデータやMeshUIのフォント経路を補完します。
- 翻訳生成時は `_TranslationWorkspace/split_1000/` を正本として扱い、生成物との整合性を検証します。
- Release ZIPには開発用の診断DLLや廃止済みのWorld Nameplate実装を含めないようチェックしています。

## ライセンス

第三者コンポーネントのライセンスについては、`THIRD_PARTY_NOTICES.md` および `third_party/` を参照してください。
