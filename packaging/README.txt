RO3 Asia 日本語化パッチ
======================

インストール
------------
1. RO3を終了します。
2. このZIPを任意のフォルダへ展開します。
3. Install-Japanese.bat を実行します。
4. ro3.exe があるRO3の Client フォルダを指定します。
   ro3.exe または Client フォルダをBATファイルへドラッグ＆ドロップして指定することもできます。
5. [OK] と表示されたら、通常どおりRO3を起動します。

以前のバージョンが導入済みの場合も、そのまま上書きインストールできます。
既存のパッチファイルは新しいReleaseの内容で更新され、既存BepInExの所有状態を示す情報は保持されます。

自動更新
--------
1. RO3を終了します。
2. 展開済みのReleaseフォルダにある Update-Japanese.bat を実行します。
3. ro3.exe があるRO3の Client フォルダを指定します。
4. GitHub Releasesから最新版を確認し、必要な場合は最新Release ZIPを自動でダウンロードして更新します。

GitHub側でSHA-256が提供されている場合は、ダウンロードしたZIPの整合性を確認してから展開します。
ダウンロードしたZIPと一時展開フォルダは更新完了後に削除されます。

古いReleaseから展開した Update-Japanese.bat も利用できます。
アップデーターは実行時にGitHub上の最新Releaseを確認します。

同梱内容
--------
- 日本語翻訳辞書
- RO3.LocalizationTablePatcher.dll
- パッチの動作に必要なBepInExランタイム
- XUnity AutoTranslator / ResourceRedirector
- 日本語表示用TextMeshProフォールバックフォントアセット
- インストーラー / アップデーター / アンインストーラー

注意事項
--------
- インストール・更新時はRO3を終了してください。
- 通常利用では翻訳正本TSVや開発用スクリプトは必要ありません。
- 問題がある場合は、最新Releaseを再度展開してからインストールまたは更新を実行してください。
