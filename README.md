# RO3 Asia Japanese Patch

RO3 Asia の日本語化パッチを、翻訳ソースから生成・検証・ゲームへ反映するための作業リポジトリです。

## 正本データ

`_TranslationWorkspace/split_1000/` の **part 1〜27** が日本語対訳の正本です。

- 行数: 26,615
- Index: 1〜26,615
- `_manifest.json` で構成を検証
- `LanguageKV_full_en.tsv` は英語テキストとゲーム内IDの参照表
- `_TranslationWorkspace/import/` は必要な場合だけ追加上書きを置く場所

翻訳修正は、原則として生成済み `.txt` を直接編集せず `split_1000` 側を直します。

## ディレクトリ構成

```text
RO3_Asia_Japanese_Patch/
├─ _TranslationWorkspace/
│  ├─ import_translations.py       # 辞書生成・検証の本体
│  ├─ LanguageKV_full_en.tsv       # LanguageKV ID参照表
│  ├─ split_1000/                  # canonical 日本語対訳 TSV part 1-27
│  └─ import/                      # optional override input
├─ Client/
│  ├─ BepInEx/config/
│  │  └─ AutoTranslatorConfig.ini
│  ├─ BepInEx/Translation/ja/Text/ # 生成済みXUnity辞書
│  └─ arialuni_sdf_u2022           # TMP日本語fallback font asset
├─ scripts/
│  ├─ Deploy-To-Game.ps1           # 実ゲームへ必要ファイルだけコピー
│  └─ Build-Release.ps1            # 導入用Release ZIPを生成
├─ packaging/                       # Release用 installer / uninstaller
├─ third_party/                     # 第三者ライセンス本文
├─ tools/diagnostics/
│  └─ WorldNameplateProbe.cs       # 敵頭上ネーム調査用。製品DLLではない
├─ Validate-Translations.bat
├─ Import-Translations.bat
└─ Build-And-Deploy.bat
```

BepInEx / XUnity AutoTranslator / ResourceRedirector 本体の第三者DLLは、このGit正本には含めません。完全配布ZIPは `dist/` のビルド成果物として別管理します。

第三者コンポーネントの出自・ライセンスは `THIRD_PARTY_NOTICES.md` と `third_party/` を参照してください。Release ZIPにも同じライセンス文書を同梱します。

## 普段の作業

1. `_TranslationWorkspace/split_1000/part_*.tsv` を修正する。
2. `Validate-Translations.bat` を実行して canonical 26,615件・placeholder 等を検証する。
3. `Import-Translations.bat` でリポジトリ内の `Client/BepInEx/Translation/ja/Text/` を再生成する。
4. `Build-And-Deploy.bat` なら生成後、そのまま現在の RO3 インストールへ反映する。
5. `git diff` で TSV と生成辞書の差分を確認してコミットする。

## Release ZIP

現在インストール済みで動作確認したBepInEx/XUnityランタイムと、このリポジトリから再生成した最新辞書を組み合わせて配布ZIPを作ります。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Release.ps1 -Version 2026.09.17.1
```

生成物は `dist/` に作られ、Gitでは追跡しません。`Install-Japanese.bat` は初回導入だけでなく、以前のReleaseを導入済みの環境への**上書き更新**にも対応しています。

## 実ゲームへの反映先

このリポジトリが現在のように

```text
C:\Path\To\RO3 Asia Launcher\RO3_Asia_Japanese_Patch
```

にある場合、`Build-And-Deploy.bat` は親フォルダをゲームルートとみなし、

```text
C:\Path\To\RO3 Asia Launcher\Client
```

へ必要な翻訳ファイルだけをコピーします。

別の場所にcloneした場合は次のように明示できます。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Deploy-To-Game.ps1 -GameRoot "C:\path\to\RO3 Asia Launcher"
```

## 生成時に維持する重要設定

`import_translations.py` は以下を維持します。

```ini
ReloadTranslationsOnFileChange=True
UseStaticTranslations=False
TextGetterCompatibilityMode=True
GeneratePartialTranslations=False
OverrideFontTextMeshPro=
FallbackFontTextMeshPro=arialuni_sdf_u2022
```

これらは JOB 表示、短いキー入力ラベル、TMP private-use glyph、日本語fallback font、runtime placeholder 翻訳の既存修正を維持するために必要です。
