# explorer_cover

C#＋WPFで外枠を作り、Windows Shellの`IExplorerBrowser`を左右に埋め込んだファイラーの試作です。プロジェクト名の`explorer_cover`は、Explorerの閲覧・操作機能を包み、配置と状態管理を自分好みにする外枠を表します。

各ペインのタブと、タブごとの戻る・進む・親へ移動（段階5b）を実装済みです。ボタンとキーは共通コマンドを使い、JSONでショートカットを変更できます。次はこの状態で試用し、改善点を確認します。

## 起動

この作業環境では、プロジェクト内の`.tools/dotnet`に.NET SDK 10.0.401を配置済みです。システムのPATH変更は不要です。

- **最初の試用:** `trial.cmd`をダブルクリックします。毎回、新しい試用フォルダーと日本語のサンプルファイルを作成して起動します。
- **通常起動:** `run.cmd`をダブルクリックします。左右にユーザーフォルダーを表示します。
- 起動時にReleaseビルドを行います。初回は少し時間がかかります。

PowerShellから左右の場所を指定する場合:

```powershell
.\run.ps1 -LeftPath 'D:\作業' -RightPath 'D:\資料'
```

別の環境では、Windows x64と.NET 10 SDK（WPFの開発に対応するWindows版）が必要です。SDKの`dotnet`がPATHにあれば、ローカルSDKがなくても起動できます。外部NuGetパッケージは使っていません。

## 操作

| 操作 | 方法 |
|---|---|
| フォルダーへ移動 | パス欄に入力してEnter、または「移動」 |
| 一覧内のフォルダーを開く | ダブルクリック、またはEnter |
| パス欄へ移る | Ctrl+L |
| 左右の一覧を切り替える | F6 |
| パス欄から一覧へ戻る | Esc |
| 一覧からパス欄へ移る | Tab／Shift+Tab |
| 新しいタブ | ＋／Ctrl+T。ペインの起動時の場所を開く |
| タブを複製 | 「複製」／Ctrl+Shift+T。現在地を新しいタブで開く（履歴はコピーしない） |
| タブを閉じる | ×／Ctrl+W。最後の1タブは残す |
| タブを切り替える | 見出しをクリック、Ctrl+Tab／Ctrl+Shift+Tab。末尾から先頭へ循環 |
| 戻る／進む | ←／→、Alt+Left／Alt+Right |
| ひとつ上へ | ↑／Alt+Up。ドライブのルートでは無効 |
| コピー／切り取り／貼り付け | Ctrl+C／Ctrl+X／Ctrl+V |
| 名前変更 | F2。編集中のCtrl+Aで拡張子を含めて全選択 |
| ごみ箱へ移動 | Delete。確認の有無はWindows側の設定に従う |
| ペイン幅変更 | 中央の境界をドラッグ |
| 右クリック／ドラッグ＆ドロップ | シェルの標準動作を利用 |

ファイル操作は実際のファイルに対して実行されます。`trial.cmd`のサンプルで基本動作を確認できます。

## 試用の流れ

1. `trial.cmd`で起動し、左右の移動、幅変更、コピー、名前変更を試します。
2. Explorerとのドラッグ＆ドロップ、普段利用している右クリック項目を試します。
3. パス欄に普段の場所を入力して、フォーカスやキー操作の使い勝手を確認します。
4. [試用メモ](docs/TRIAL.md)に気付いた点を記録します。
5. タブを複製して別の場所へ移動し、切替・戻る・進むを試します。[開発計画](PLAN.md)に追加したタブのマウス操作（5b-2）を実装・再試用し、その後にQuickLook連携へ進みます。

このPCではAutoHotkeyが左Ctrl+Tabをウィンドウ切替に割り当てています。アプリの初期キーを試す場合は右Ctrlを使うか、下記のJSONで`nextTab`／`previousTab`をF8／F7などに変更してください。常駐ツールの設定は変更していません。

## ショートカットの変更（設定画面は後の段階）

起動時に`%LOCALAPPDATA%\explorer_cover\shortcuts.json`があれば読み込みます。ファイルがなければ従来の初期値を使います。アプリはこの段階では設定ファイルを自動生成・上書きしません。

まずプロジェクト内のサンプルで試す場合:

```powershell
$env:EXPLORER_COVER_SHORTCUTS = "$PWD\config\shortcuts.example.json"
.\run.ps1
```

サンプルではパス入力をCtrl+K、左右切替をF8に変更します。指定したコマンドの初期割り当ては置き換わり、省略したコマンドは初期値を維持します。空配列`[]`でそのコマンドのショートカットを解除できます。設定ファイルの変更は再起動時に反映されます。通常の設定ファイルへ戻すには環境変数を解除します。

```powershell
Remove-Item Env:\EXPLORER_COVER_SHORTCUTS
```

設定形式:

```json
{
  "version": 1,
  "bindings": {
    "focusAddress": ["Ctrl+K"],
    "switchPane": ["F8"],
    "navigateAddress": ["Enter"],
    "focusFiles": ["Escape"]
  }
}
```

| コマンドID | 操作 | 初期キー | 対象 |
|---|---|---|---|
| `focusAddress` | パス入力へ移動 | Ctrl+L | 一覧・パス欄・外枠 |
| `switchPane` | 左右の一覧を切り替え | F6 | 一覧・パス欄・外枠 |
| `navigateAddress` | 入力したパスへ移動 | Enter | パス欄 |
| `focusFiles` | パス編集を取り消して一覧へ戻る | Escape | パス欄 |
| `newTab` | 新しいタブ | Ctrl+T | 一覧・パス欄・外枠 |
| `duplicateTab` | 現在地を複製 | Ctrl+Shift+T | 一覧・パス欄・外枠 |
| `closeTab` | 選択中タブを閉じる | Ctrl+W | 一覧・パス欄・外枠 |
| `nextTab` | 次のタブ | Ctrl+Tab | 一覧・パス欄・外枠 |
| `previousTab` | 前のタブ | Ctrl+Shift+Tab | 一覧・パス欄・外枠 |
| `back` | 戻る | Alt+Left | 一覧・パス欄・外枠 |
| `forward` | 進む | Alt+Right | 一覧・パス欄・外枠 |
| `parent` | ひとつ上へ | Alt+Up | 一覧・パス欄・外枠 |

同じ入力範囲での重複、誤ったコマンド名、未対応のキーやバージョンは拒否します。不正な設定は一部だけ適用せず、初期値で起動して画面下部に警告を表示します。通常のキー案内も実際の設定に追従します。

この段階ではCtrl+C／X／Vなどのシェル操作、F2・F5・F10、編集キー、Space、Ctrl+Alt、Winキーを含む割り当ては変更対象外です。Tab・カーソルキーはCtrl+Tab／Ctrl+Shift+Tab、Alt+Left／Right／Upのみ割り当て可能です。一覧からパス欄へのTab／Shift+Tabは固定のフォーカス移動として残ります。名前変更欄とIME変換中はアプリのコマンドで入力を奪いません。QuickLook用のSpaceは段階5cで追加します。

## 現時点の制限

- 終了時の状態保存・復元は未実装です。タブと履歴は終了すると失われます。
- タブごとにシェルビューを保持するため、開く数に応じてメモリ使用量が増えます。タブの並べ替え・閉じたタブの復元は未実装です。
- パス欄はファイルシステムのフォルダーを対象としています。仮想フォルダーの文字列表現を直接入力する機能はありません。
- 右クリックは今回の検証環境では従来型のシェルメニューです。Windows 11の新メニューと同一ではありません。
- ネットワーク、クラウド、すべてのシェル拡張、異なるDPIの複数モニター間移動は未検証です。
- アクセスできないパスはペイン下部にエラーを表示します。時間のかかるネットワークパスなどでの非同期化は今後の検討対象です。
- 試用フォルダーは自動削除しません。

## 開発と検証

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.tools\cli"
.\.tools\dotnet\dotnet.exe build .\src\ExplorerCover\ExplorerCover.csproj -c Release
.\.tools\dotnet\dotnet.exe run --project .\tests\ExplorerCover.Core.Tests -c Release
```

検証結果は[検証記録](docs/VERIFICATION.md)に記載しています。`scripts/Verify-*.ps1`はWindowsのUI Automationを使う開発用スクリプトです。デスクトップ上の試作用ウィンドウと検証用フォルダーを操作するため、通常の操作と同時に実行しないでください。試用はこれらを実行せず`trial.cmd`だけで始められます。

診断ログが必要な場合は、起動前に`EXPLORER_COVER_LOG`へ書き込み可能なログファイルの絶対パスを指定します。初期化、移動、エラー、終了処理を記録します。通常起動ではログを書きません。

## 構成

- `src/ExplorerCover/MainWindow.cs`: 2ペイン配置とキーの振り分け。
- `src/ExplorerCover/BrowserPane.cs`: タブとビューの寿命、パス入力、履歴移動、状態表示、フォーカス。
- `src/ExplorerCover/Shell/ExplorerHost.cs`: HWND、COM、シェルのイベントと寿命管理。
- `src/ExplorerCover/Shell/Native.cs`: Windows SDKに基づくCOMとWin32の定義。
- `src/ExplorerCover.Core/`: WPF非依存のウィンドウ・ペイン・タブ・履歴、コマンド定義、キー設定の解析・検証。
- `src/ExplorerCover/Commands/`: WPFボタンへの接続と設定ファイルの読み込み。
- `tests/ExplorerCover.Core.Tests/`: 追加パッケージ不要のコンソール形式のテスト。失敗時は非ゼロで終了。

構成と次の段階での拡張箇所は[基盤の設計](docs/FOUNDATION.md)に記載しています。

シェルのメソッド定義は[MicrosoftのWindows SDKヘッダー](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/ShObjIdl_core.h)を参照しています。
