# RO3 Asia Japanese Patch

RO3 Asia（PC版）向け日本語化パッチのデータ管理・辞書生成・配信用リポジトリです。

- **最新リリース**: [Releases](https://github.com/MiKoP4/RO3_Asia_Japanese_Patch/releases)

---

## 翻訳の修正・反映手順（通常作業）

1. **翻訳を修正**:
   `_TranslationWorkspace/split_1000/part_*.tsv` を編集します。
   ※ 生成物の `.txt` ではなく、必ず **TSV正本** を編集してください。

2. **検証**:
   ```bat
   Validate-Translations.bat
   ```

3. **反映（辞書生成 & ゲームへコピー）**:
   ```bat
   Build-And-Deploy.bat
   ```
   - 辞書生成のみ行う場合は `Import-Translations.bat` を実行。
   - 本リポジトリがゲームフォルダ直下にある場合、親フォルダの `Client/` へ自動反映されます。

---

## 配布用 Release ZIP の作成

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Release.ps1 -Version <バージョン名>
```
- `dist/` に配布用ZIP（インストーラー同梱）が生成されます。

---

## ディレクトリ構成

- `_TranslationWorkspace/` : 翻訳データ（正本 TSV: `split_1000/`）および辞書生成スクリプト
- `Client/` : ゲームへ配置する辞書ファイル（`BepInEx/Translation/ja/Text/`）および設定
- `src/` / `scripts/` : 補助プラグイン（XUnity迂回表示対応）ソースおよび自動化スクリプト
- `packaging/` : リリース用インストーラー等

---

## 技術的な留意事項

- **正本管理**: 生成済みテキスト（`Client/.../*.txt`）は直接編集せず、`_TranslationWorkspace/split_1000/` の TSV を正本として扱います。
- **XUnity 迂回テキスト**: モンスター頭上ネームや一部HUDなど XUnity を経由しない表示は、補助プラグイン `RO3.LocalizationTablePatcher.dll` により対応を進めています。
