# 大容量コピー中の操作と終了

実装・検証日: 2026-10-03

Windows Shellへ非同期実行に必要なスレッド参照を提供し、大容量コピー中もタブや別ペインを操作できるようにしました。コピー・移動、進捗画面、同名確認は引き続きWindows Shellが処理します。

## 修正の比較と優先順位

| 優先順位 | 対象 | 修正内容と判断 |
|---|---|---|
| 1 | 3経路の共通基盤 | UIのSTAに`SHCreateThreadRef`／`SHSetThreadRef`を登録し、Shellが非同期処理を開始できるようにする。終了時もメッセージ処理を継続する |
| 2 | Ctrl+V、および設定で割り当てた貼り付けキー | `ExplorerHost.ExecuteShellCommand`の`paste`に`CMIC_MASK_ASYNCOK`を指定する |
| 2 | 標準の右クリックメニューの貼り付け | 共通のスレッド参照で改善することを実画面で確認した。標準メニューを引き続き利用する |
| 3 | タブ見出しへのドロップ | ローカル宛ては共通基盤で改善した。WSLが関わる通常ファイルのコピーは、下記の専用STAで実行する |

非同期フラグだけでは不十分で、呼び出し元のスレッド参照も必要です。[Microsoftの説明](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-invokecommand)を参照してください。このフラグはShellへの許可であり、すべての拡張機能の非同期実行を保証するものではありません。

事前調査ではWPFの`DataObject`経由で元データの`IDataObjectAsyncCapability`を取得できなくなることも確認しました。ただし、標準のファイルシステムへのコピーではスレッド参照の追加で実際の停止を改善できました。この違いだけを根拠に、ドロップ受付の全面変更は行っていません。

## スレッド参照と終了

`ShellThreadLifetime`がUIのSTAのスレッド参照と、参照カウンター用のネイティブメモリを所有します。登録は`App.OnStartup`で最初のShellビューを作る前に行います。カウンターはローカル変数や移動可能なマネージドメモリに置きません。

閉じる操作では作業状態を保存し、各タブのShellビューを解放します。ビュー自体もスレッド参照を保持するため、ビューを残したまま参照数の減少を待つと終了できません。

続いて、アプリが所有するクリップボードだけを`OleFlushClipboard`で確定します。コピーしたデータの形式を残しながら、クリップボードが保持するCOM参照を解放する処理です。他のプロセスが所有するクリップボードにはこの処理を行いません。[Microsoftの説明](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleflushclipboard)を参照してください。

WSLコピーの専用STAが残っている場合は、先にその完了を待ちます。続いて、ウィンドウ破棄後も回収待ちのドロップデータがShellを参照している場合は、終了時だけ二巡のGCとRCWの解放を行います。WPFのラッパーを回収した一巡目には内側のRCWがまだ残り、二巡目でスレッド参照を解放できることを専用テストで確認しました。Finalizerの終了は別スレッドで待ち、UIのSTAはCOMの呼び戻しを処理できるようにします。

そのあとShellの非同期処理が残っていれば、50 msの非同期待機でDispatcherを動かし続けます。ウィンドウは閉じますが、プロセスはコピーなどの完了まで残ります。Shellの参照が解放されてから`Application.Shutdown`を呼び、同じSTA上でスレッド参照とカウンターを解放します。UIで同期的な`Wait`や`Join`は使いません。

## WSLのタブドロップ

共通基盤の修正後も、WindowsからWSL 2のUbuntu-24.04へ1 GiBをコピーすると、Shellの`Drop`が約3.4秒戻らず、メインウィンドウの応答が止まることを再現しました。非同期実行の許可だけではこの経路を改善できませんでした。

Shellがコピーと判定し、元データが`FileDrop`形式のパス一覧を提供する場合、コピー元と宛先のいずれかが`\\wsl.localhost\`または`\\wsl$\`なら、`ShellCopyOperation`がパス一覧とドロップ時の宛先を保持します。別のSTAで`IShellItem`と`IFileOperation`を作成し、標準の進捗・同名確認・取消を使ってコピーします。UIで作成したCOMオブジェクトを別スレッドへ渡す方式にはしていません。[IFileOperationの仕様](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)を参照してください。

ドロップ元にはコピーの受理を返し、転送完了とは区別します。元データの削除を指示する移動の結果は返しません。確認画面の所有者にメインウィンドウを指定しないため、確認中もタブを操作できます。取消は標準画面で扱い、例外はペイン下部に「コピーできません: …」を表示します。

この追加処理は通常のファイル・フォルダーのコピーに限ります。移動、リンク、右ボタンドラッグ、パス一覧を持たない仮想ファイルは従来のShellドロップに委譲します。WSLでのこれらの応答性、およびWSL宛てのCtrl+V・右クリック貼り付けは今回の追加修正では保証していません。

## 検証結果

利用者の許可を得て、専用フォルダー内のファイルだけで実画面を操作しました。コピー先を排他的に開けないことを確認し、コピー後の操作だけで成功扱いにしないようにしています。

| 操作 | サイズ | 結果 |
|---|---:|---|
| 同じタブ・同じフォルダー内のCtrl+V | 1 GiB | コピー中のウィンドウ応答と別ペインのタブ追加、完了後のSHA-256一致 |
| 同じタブ・同じフォルダー内の右クリック貼り付け | 1 GiB | コピー中のウィンドウ応答と別ペインのタブ追加、完了後のSHA-256一致 |
| 非選択タブ見出しへのCtrlドロップ | 4 GiB | コピー中のウィンドウ応答とタブ追加、コピー元の保持、完了後のSHA-256一致 |
| コピー中にウィンドウを閉じる | 4 GiB | コピー完了後のSHA-256一致とプロセスの正常終了 |
| Windowsから非選択のWSLタブへ修飾キーなしでドロップ | 1 GiB | コピー中の応答とタブ追加、コピー元の保持、WindowsとWSLで計算したSHA-256一致 |
| WindowsからWSLタブへCtrlドロップし、コピー中に閉じる | 1 GiB | コピー内容のSHA-256一致とプロセスの正常終了 |
| WSL宛ての同名確認を開き、閉じて取消 | 小容量テキスト | 確認中の応答とタブ追加、既存の宛先内容とコピー元の保持、取消後の正常終了 |

共通基盤の修正時はCore 47/47、TabDrop 16/16、Sidebar 10/10、ShellOperations 3/3、Selection 7/7、QuickLook 8/8、Persistence 10/10、Navigation 4項目が成功しました。WSL追加修正ではTabDrop 20/20、ShellOperations 6/6が成功しました。コピーの先行受理、宛先の保持、WSL別名とコピー元側のWSL判定、移動・右ドラッグの従来委譲、失敗表示、複数ファイルとフォルダーの実コピー、終了待ちとRCW解放を確認します。

再検証は[Verify-FileOperationResponsiveness.ps1](../scripts/Verify-FileOperationResponsiveness.ps1)を使用します。起動ごとに`artifacts/file-operations-<ID>/`を作り、検証対象のプロセスとパスを確認してから操作します。マウス・キーを使用し、一時的にクリップボードを変更するため、実行前に利用者の許可を得てください。

```powershell
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Start -SizeMiB 1024
./scripts/Verify-FileOperationResponsiveness.ps1 -Step CtrlPaste
./scripts/Verify-FileOperationResponsiveness.ps1 -Step MenuPaste
./scripts/Verify-FileOperationResponsiveness.ps1 -Step TabDrop
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Close

# WSL 2・Ubuntu-24.04でのコピーと同名確認・取消
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Start -SizeMiB 1024 -WslDistro Ubuntu-24.04
./scripts/Verify-FileOperationResponsiveness.ps1 -Step TabDrop -WithoutCtrl -NonSelectedTab
./scripts/Verify-FileOperationResponsiveness.ps1 -Step ConflictCancel
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Close

# 別セッションでコピー中の終了を確認
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Start -SizeMiB 1024 -WslDistro Ubuntu-24.04
./scripts/Verify-FileOperationResponsiveness.ps1 -Step TabDrop -CloseDuringCopy
```

検証時点でコピーが完了してしまった場合は、サイズを増やして新しいセッションで再検証します。タブドロップを繰り返すと同名確認が出るため、コピー先の専用ファイルを片付けるか、新しいセッションを使用します。

WSLの最終ビルドの実画面ログは`artifacts/file-operations-069aeb2779654aa5a253993e32b169c3/`と`artifacts/file-operations-8ddd77091e1b471db1836786dd241c7b/`です。専用WSL一時フォルダーのパスも各ログフォルダーへ記録します。

WSL以外のUNC・クラウド・仮想ファイル、大容量の移動、WSLからWindowsへの大容量コピー、大容量転送の進捗画面からの取消は未検証です。今回の実画面の取消検証は同名確認での取消です。問題が残る経路では、元のOLEデータを直接受け取る方式などを追加検討します。
