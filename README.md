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
│  │  ├─ AutoTranslatorConfig.ini
│  │  └─ RO3.LocalizationOverrides.tsv # LanguageKV ID単位のruntime補助マップ
│  ├─ BepInEx/plugins/
│  │  └─ RO3.LocalizationTablePatcher.dll # Localization_en補助パッチ
│  ├─ BepInEx/Translation/ja/Text/ # 生成済みXUnity辞書
│  └─ arialuni_sdf_u2022           # TMP日本語fallback font asset
├─ src/RO3.LocalizationTablePatcher/ # Localization_en補助プラグインのソース
├─ scripts/
│  ├─ Build-LocalizationTablePatcher.ps1 # runtime補助DLLをビルド
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

BepInEx / XUnity AutoTranslator / ResourceRedirector の配布用ランタイム一式は、このGit正本には含めません。`third_party/reference/BepInEx.dll` のみ、補助プラグインを再ビルドするためのクリーンなコンパイル参照として保持します。完全配布ZIPは `dist/` のビルド成果物として別管理します。

第三者コンポーネントの出自・ライセンスは `THIRD_PARTY_NOTICES.md` と `third_party/` を参照してください。Release ZIPにも同じライセンス文書を同梱します。

## 普段の作業

1. `_TranslationWorkspace/split_1000/part_*.tsv` を修正する。
2. `Validate-Translations.bat` を実行して canonical 26,615件・placeholder 等を検証する。
3. `Import-Translations.bat` でリポジトリ内の `Client/BepInEx/Translation/ja/Text/` を再生成する。
4. `Build-And-Deploy.bat` なら生成後、そのまま現在の RO3 インストールへ反映する。
5. `git diff` で TSV と生成辞書の差分を確認してコミットする。

## Release ZIP

公開済みReleaseは次から取得できます。

https://github.com/MiKoP4/RO3_Asia_Japanese_Patch/releases

現在インストール済みで動作確認したBepInEx/XUnityランタイムと、このリポジトリから再生成した最新辞書を組み合わせて配布ZIPを作ります。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Release.ps1 -Version 2026.09.17.1
```

生成物は `dist/` に作られ、Gitでは追跡しません。`Install-Japanese.bat` は初回導入だけでなく、以前のReleaseを導入済みの環境への**上書き更新**にも対応しています。

## XUnity を通らない runtime 表示

RO3 には、XUnity 5.6.2 の TextMeshPro setter hook を通らない表示経路があります。代表例はモンスター/NPCの頭上ネーム、戦闘時の一部スキル吹き出し、タスクHUDの一部です。

`RO3.LocalizationTablePatcher.dll` は、XUnity を迂回する表示経路を追跡するための runtime 補助プラグインです。`RO3.LocalizationOverrides.tsv` の形式は `ID<TAB>English<TAB>Japanese` で、canonical Japanese は `_TranslationWorkspace/split_1000/` parts 1–27 から生成します。C# 側の LanguageKV キャッシュにはこのIDマップを安全に反映しますが、モンスター/NPCの頭上ネームは別の Lua/MeshUI 経路を通ることが確認されており、そこへの適用は現在も調査中です。

対象には、報告済みの不具合に関係する namespace（スキル名/説明、NPC・モンスター名、イベント、クエスト、Suggested Build、Spirit Tower など）だけを含めます。プラグイン側に第二の手書き辞書は持ちません。`Piere → ピエール`、`Magnolia → マグノリア`、`Ahn Gu-ho → アン・グホ`、`Alphonse → アルフォンス` などは生成時の回帰ガードに含まれます。

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
