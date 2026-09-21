# 日常利用と更新

## 起動

通常版は`dist\explorer_cover\explorer_cover.exe`をダブルクリックして起動する。毎回のビルド、PowerShell、.NET SDKの起動は不要。Windows x64向けの.NETランタイムを同梱している。

exeへのショートカットを作成して利用できる。exe単体を移動せず、必要な場合は`explorer_cover`フォルダー全体をコピーする。QuickLookは別途インストール済みのものを使用する。

作業状態・設定は従来と同じ`%LOCALAPPDATA%\explorer_cover`へ保存する。通常版と開発版で共有するため、同時起動を避ける。保存先指定の環境変数も引き続き使用できる。

## 作成・更新

1. 通常版のexplorer_coverを閉じる。
2. ソースコードのフォルダーで`publish.cmd`を実行する。
3. 「発行完了」と表示されたら、同じ`dist\explorer_cover\explorer_cover.exe`を開く。

発行時だけWindows用.NET 10 SDKが必要。プロジェクト内の`.tools\dotnet`を優先し、なければPATHのdotnetを使う。初回はランタイムパッケージを取得するためネット接続が必要。

新しい版は別フォルダーで作成し、成功後に切り替える。ビルドに失敗しても現在の通常版は維持する。実行中の通常版は終了を促し、強制終了しない。同時の発行はロックで防ぐ。

更新前の版は`dist\previous-日時-ID`へ退避する。設定と作業状態は更新時に変更しない。問題がある場合は新しい版を閉じ、退避フォルダー内のexeを起動できる。ただし、将来保存形式が変わった場合の旧版との互換性は別途確認が必要。

前の版と失敗時の`.build-*`フォルダーは自動削除しない。不要になったものはアプリを閉じてからExplorerで削除できる。.NETの修正も同梱するには、SDK等を更新して再発行する。

## 開発・試用との使い分け

- `run.cmd`: ソースをビルドして起動する開発用。変更後の確認に使う。
- `trial.cmd`: サンプルフォルダーを作って起動する一時試用用。
- `publish.cmd`: 日常利用版を作成・更新する。普段の起動は生成されたexeを使う。

方式の参照: [.NETの自己完結型発行](https://learn.microsoft.com/en-us/dotnet/core/deploying/#publish-as-self-contained)。
