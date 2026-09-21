# explorer_cover

C#＋WPFで外枠を作り、Windows Shellの`IExplorerBrowser`を左右に埋め込んだファイラーの試作です。プロジェクト名の`explorer_cover`は、Explorerの閲覧・操作機能を包み、配置と状態管理を自分好みにする外枠を表します。

各ペインのタブと、タブごとの戻る・進む・親へ移動（段階5b）を実装済みです。ボタンとキーは共通コマンドを使い、設定画面でショートカットを変更できます。次はこの状態で試用し、改善点を確認します。

段階5b-2のタブのマウス操作も実装・検証済みです。並べ替え、空白の左ダブルクリックによる追加、設定したボタンでの終了を試用できます。[検証記録](docs/TAB-MOUSE.md)を参照してください。

段階5cのQuickLook連携を追加しました。一覧でファイルを1つ選び、Spaceを押して離すとプレビューします。[設定・検証記録](docs/QUICKLOOK.md)を参照してください。

プレビューを開いた後は、一覧の選択を変えると表示内容も自動で切り替わります。閉じた後は選択変更だけで開き直しません。

段階6aの左サイドビューを追加しました。ブックマークとドライブ容量を表示し、クリックした場所を直前にフォーカスしていたタブで開きます。[操作・検証記録](docs/SIDEBAR.md)を参照してください。

6a-2ではドライブを上、ブックマークを下へ配置し、枠線のない行表示とShell標準アイコンに変更しました。未接続ドライブは非表示にし、非アクティブ側は外枠を灰色にしています。[外観改善の記録](docs/SIDEBAR-APPEARANCE.md)を参照してください。

6a-3ではWSLのUNCパスに対応しました。パス欄へ`\\wsl.localhost\Ubuntu-24.04\home`などを入力できます。大小文字の異なるフォルダーを区別し、移動先の確認はバックグラウンドで行います。[使い方・検証・制限](docs/WSL.md)を参照してください。

6bではタブ・パス・選択中タブ・アクティブペイン・各幅・ブックマークを自動保存し、通常起動時に復元します。[保存内容と検証記録](docs/PERSISTENCE.md)を参照してください。

## 起動

この作業環境では、プロジェクト内の`.tools/dotnet`に.NET SDK 10.0.401を配置済みです。システムのPATH変更は不要です。

- **最初の試用:** `trial.cmd`をダブルクリックします。毎回、新しい試用フォルダーと日本語のサンプルファイルを作成して起動します。
- **通常起動・保存復元の試用:** `run.cmd`をダブルクリックします。前回の状態を復元します。初回は左右にユーザーフォルダーを表示します。
- `trial.cmd`やパスを明示した起動は一時セッションです。普段の保存状態を上書きしません。
- 起動時にReleaseビルドを行います。初回は少し時間がかかります。

PowerShellから左右の場所を指定する場合:

```powershell
.\run.ps1 -LeftPath 'D:\作業' -RightPath 'D:\資料'
```

別の環境では、Windows x64と.NET 10 SDK（WPFの開発に対応するWindows版）が必要です。SDKの`dotnet`がPATHにあれば、ローカルSDKがなくても起動できます。外部NuGetパッケージは使っていません。

## 操作

| 操作 | 方法 |
|---|---|
| フォルダーへ移動 | パス欄に入力してEnter、またはパス欄右端の→ |
| 一覧内のフォルダーを開く | ダブルクリック、またはEnter |
| パス欄へ移る | Ctrl+L |
| 左右の一覧を切り替える | F6 |
| パス欄から一覧へ戻る | Esc |
| 一覧からパス欄へ移る | Tab／Shift+Tab |
| 新しいタブ | ＋／Ctrl+T。ペインの起動時の場所を開く |
| タブを複製 | 重なった四角のアイコン／Ctrl+Shift+T。現在地を新しいタブで開く（履歴はコピーしない） |
| タブを閉じる | ×／Ctrl+W。最後の1タブは残す |
| タブを切り替える | 見出しをクリック、Ctrl+Tab／Ctrl+Shift+Tab。末尾から先頭へ循環 |
| タブの順序を変える | 見出しを左ドラッグ。同じペイン内で移動、Escape／バー外へのドロップで取消 |
| 見出しからタブを閉じる | 初期値は中クリック。選択中でないタブも直接閉じられる |
| 空白部分から新規タブ | タブバーの空白を左ダブルクリック |
| 戻る／進む | ←／→、Alt+Left／Alt+Right |
| ひとつ上へ | ↑／Alt+Up。ドライブのルートでは無効 |
| QuickLookでプレビュー | 一覧で単一ファイルを選択してSpace。もう一度押すと閉じる |
| コピー／切り取り／貼り付け | Ctrl+C／Ctrl+X／Ctrl+V |
| 名前変更 | F2。編集中のCtrl+Aで拡張子を含めて全選択 |
| ごみ箱へ移動 | Delete。確認の有無はWindows側の設定に従う |
| ペイン幅変更 | 中央の境界をドラッグ |
| サイドビューから移動 | ブックマーク／ドライブをクリック。直前にフォーカスしていたタブで開く |
| ブックマーク登録 | 「ブックマーク」見出しの右端の＋ |
| ブックマーク編集 | 項目の右クリックから名前変更・削除 |
| サイドビュー幅変更 | サイドビュー右側の境界をドラッグ |
| ドライブ容量更新 | 「ドライブ」見出しの右端の↻、または10秒ごとの自動更新。未接続は非表示 |
| 右クリック／ドラッグ＆ドロップ | シェルの標準動作を利用 |

ファイル操作は実際のファイルに対して実行されます。`trial.cmd`のサンプルで基本動作を確認できます。

## 試用の流れ

1. `trial.cmd`で起動し、左右の移動、幅変更、コピー、名前変更を試します。
2. Explorerとのドラッグ＆ドロップ、普段利用している右クリック項目を試します。
3. パス欄に普段の場所を入力して、フォーカスやキー操作の使い勝手を確認します。
4. [試用メモ](docs/TRIAL.md)に気付いた点を記録します。
5. 保存・復元は`run.cmd`で試します。タブ・幅・ブックマークを変更して閉じ、もう一度起動して確認します。[開発計画](PLAN.md)に従い、設定画面（7）も実装済みです。左下の⚙から変更して試用します。

このPCではAutoHotkeyが左Ctrl+Tabをウィンドウ切替に割り当てています。アプリの初期キーを試す場合は右Ctrlを使うか、下記のJSONで`nextTab`／`previousTab`をF8／F7などに変更してください。常駐ツールの設定は変更していません。

## 設定画面とショートカットの変更

左下の⚙から設定画面を開けます。キーは`Ctrl+T`形式で入力し、複数なら` / `で区切ります。空欄で解除できます。タブを閉じるマウスボタン、QuickLookの起動先も変更できます。「保存」で即時反映し、再起動後も維持します。「初期値に戻す」も保存するまで反映しません。

保存先は`%LOCALAPPDATA%\explorer_cover\input.json`です。作業状態とは別に保存し、置換前の設定は`.bak`に残します。保存に失敗した場合は設定を適用せず、画面にエラーを表示します。[設定の仕様・検証](docs/SETTINGS.md)を参照してください。

以下のJSONは従来形式です。`input.json`がない場合に`shortcuts.json`・`mouse.json`・`quicklook.json`を読み込み、設定画面で保存するとまとめて`input.json`へ移行します。その後は`input.json`を優先します。

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
| `copy` | 選択項目をコピー | Ctrl+C | 一覧 |
| `cut` | 選択項目を切り取り | Ctrl+X | 一覧 |
| `paste` | 現在のフォルダーへ貼り付け | Ctrl+V | 一覧 |
| `delete` | 選択項目を削除 | Delete | 一覧 |
| `rename` | 選択項目の名前変更 | F2 | 一覧 |
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
| `quickView` | QuickLookでプレビュー | Space | 一覧（文字編集中を除く） |

同じ入力範囲での重複、誤ったコマンド名、未対応のキーやバージョンは拒否します。不正な設定は一部だけ適用せず、初期値で起動して画面下部に警告を表示します。通常のキー案内も実際の設定に追従します。

コピー・切り取り・貼り付け・削除・名前変更は一覧内で変更可能です。Ctrl+C／X／V・Delete・F2は、それぞれ元の操作の初期割り当てとしてのみ使用できます。F5・F10、その他の編集キー、Ctrl+Alt、Winキーを含む割り当ては変更対象外です。Spaceは修飾キーなしで`quickView`にのみ使用できます。Tab・カーソルキーはCtrl+Tab／Ctrl+Shift+Tab、Alt+Left／Right／UpとCtrl+PageUp／Ctrl+PageDownのみ割り当て可能です。一覧からパス欄へのTab／Shift+Tabは固定のフォーカス移動として残ります。名前変更欄とIME変換中はアプリのコマンドで入力を奪いません。

## QuickLookの設定

起動済みのQuickLookには設定なしで接続します。未起動の場合はStore版、または標準的なインストール先のQuickLookを検出して起動します。検出できない場合は`%LOCALAPPDATA%\explorer_cover\quicklook.json`でQuickLook.exeの絶対パスを指定できます。

```json
{ "version": 1, "executablePath": "C:\\Tools\\QuickLook\\QuickLook.exe" }
```

`executablePath`を省略、または`null`にすると自動検出です。環境変数`EXPLORER_COVER_QUICKLOOK`で設定ファイルを指定することもできます。設定変更は再起動時に反映します。プレビューキーの変更・解除と起動先の指定には設定画面を使えます。

未選択・複数選択・フォルダーは対象外です。パス入力と名前変更中のSpaceは文字入力として扱います。プレビュー中も一覧にフォーカスがある場合は同じキーで閉じられます。QuickLookウィンドウを操作している場合はQuickLook側のSpace／Escapeで閉じます。

## タブを閉じるマウスボタンの設定

タブを閉じるマウスボタンは、起動時に`%LOCALAPPDATA%\explorer_cover\mouse.json`から読み込みます。指定できる値は`middle`（初期値）、`right`、`xButton1`、`xButton2`、`none`（見出しのクリックによる終了を無効化）です。左ボタンは選択・ドラッグに使います。

```json
{ "version": 1, "closeTabButton": "right" }
```

設定画面では保存後すぐに反映できます。従来形式のJSONを使う場合は再起動が必要です。プロジェクト内のサンプルを使う場合は、次の環境変数で指定できます。割り当てはタブ見出しだけに適用し、一覧のシェル操作や×ボタン・Ctrl+Wは維持します。

```powershell
$env:EXPLORER_COVER_MOUSE = "$PWD\config\mouse.example.json"
.\run.ps1
# 通常の設定ファイルへ戻す場合:
Remove-Item Env:\EXPLORER_COVER_MOUSE
```

マウス設定の誤記や読み込み失敗は画面で通知し、初期値の中クリックを使います。キー設定は別のファイルとして読み込みます。

## 現時点の制限

- 通常起動ではタブ・場所・選択中タブ・各幅・ブックマークを保存・復元します。ウィンドウの位置・サイズ・最大化状態も復元します。戻る／進むの履歴、一覧内の選択とスクロールは起動をまたいで復元しません。
- 保存先は`%LOCALAPPDATA%\explorer_cover\workspace.json`です。一時セッションでは保存しません。
- タブごとにシェルビューを保持するため、開く数に応じてメモリ使用量が増えます。左右ペイン間のタブ移動・別ウィンドウへの切り離し・閉じたタブの復元は未実装です。
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
.\.tools\dotnet\dotnet.exe run --project .\tests\ExplorerCover.QuickLook.Tests -c Release
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
