# RO3 Asia 日本語化パッチ

RO3 Asia（PC版）を日本語で遊ぶための非公式パッチです。

## ダウンロード

最新版は [GitHub Releases](https://github.com/MiKoP4/RO3_Asia_Japanese_Patch/releases/latest) からダウンロードしてください。通常利用では、このリポジトリをCloneする必要はありません。

## インストール

1. RO3を終了します。
2. 最新Releaseの `RO3_Asia_Japanese_Patch_*.zip` を展開します。
3. `Install-Japanese.bat` を実行します。
4. `ro3.exe` があるRO3の `Client` フォルダを指定します。`ro3.exe` または `Client` フォルダをBATへドラッグ＆ドロップして指定することもできます。
5. `[OK] Japanese patch installed` と表示されたら、RO3を起動します。

初回導入時に1回のBAT実行だけでは翻訳が反映されない場合があります。その場合は、RO3を終了した状態で `Install-Japanese.bat` をもう一度実行し、RO3を再起動してください。

以前のバージョンが導入済みの場合も、同じ手順で上書きできます。

## 更新

1. RO3を終了します。
2. 展開済みReleaseフォルダの `Update-Japanese.bat` を実行します。
3. `ro3.exe` があるRO3の `Client` フォルダを指定します。

アップデーターはGitHub Releasesの最新版を確認し、必要な場合だけダウンロードして更新します。SHA-256ファイルが提供されている場合は、整合性を確認してから更新します。

## アンインストール

RO3を終了し、Releaseフォルダの `Uninstall-Japanese.bat` を実行してゲームの `Client` フォルダを指定してください。パッチ導入前から存在した共有BepInEx環境は自動削除しません。

## 注意事項

- インストール・更新・アンインストールの前にRO3を終了してください。
- 本パッチは非公式のコミュニティ製です。ゲームの利用規約、アンチチート、ゲーム更新との互換性は保証されません。
- 利用による起動不能、データ破損、利用制限、アカウント停止などの不利益について、適用法令により免責できない責任を除き責任を負いません。
- ゲーム更新後に問題が起きた場合は、利用を中止して対応状況を確認してください。

第三者コンポーネントのライセンスは、Release ZIPの `THIRD_PARTY_NOTICES.md` と `licenses` フォルダを確認してください。
