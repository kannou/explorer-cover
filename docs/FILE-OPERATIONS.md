# 大容量コピー中の操作と終了

実装・検証日: 2026-10-03

Windows Shellへ非同期実行に必要なスレッド参照を提供し、大容量コピー中もタブや別ペインを操作できるようにしました。コピー・移動、進捗画面、同名確認は引き続きWindows Shellが処理します。

## 修正の比較と優先順位

| 優先順位 | 対象 | 修正内容と判断 |
|---|---|---|
| 1 | 3経路の共通基盤 | UIのSTAに`SHCreateThreadRef`／`SHSetThreadRef`を登録し、Shellが非同期処理を開始できるようにする。終了時もメッセージ処理を継続する |
| 2 | Ctrl+V、および設定で割り当てた貼り付けキー | `ExplorerHost.ExecuteShellCommand`の`paste`に`CMIC_MASK_ASYNCOK`を指定する |
| 2 | 標準の右クリックメニューの貼り付け | 共通のスレッド参照で改善することを実画面で確認した。標準メニューを引き続き利用する |
| 3 | タブ見出しへのドロップ | 共通基盤だけで改善することを実画面で確認した。既存のShell委譲を維持する |

非同期フラグだけでは不十分で、呼び出し元のスレッド参照も必要です。[Microsoftの説明](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-invokecommand)を参照してください。このフラグはShellへの許可であり、すべての拡張機能の非同期実行を保証するものではありません。

事前調査ではWPFの`DataObject`経由で元データの`IDataObjectAsyncCapability`を取得できなくなることも確認しました。ただし、標準のファイルシステムへのコピーではスレッド参照の追加で実際の停止を改善できました。この違いだけを根拠に、ドロップ受付の全面変更は行っていません。

## スレッド参照と終了

`ShellThreadLifetime`がUIのSTAのスレッド参照と、参照カウンター用のネイティブメモリを所有します。登録は`App.OnStartup`で最初のShellビューを作る前に行います。カウンターはローカル変数や移動可能なマネージドメモリに置きません。

閉じる操作では作業状態を保存し、各タブのShellビューを解放します。ビュー自体もスレッド参照を保持するため、ビューを残したまま参照数の減少を待つと終了できません。

続いて、アプリが所有するクリップボードだけを`OleFlushClipboard`で確定します。コピーしたデータの形式を残しながら、クリップボードが保持するCOM参照を解放する処理です。他のプロセスが所有するクリップボードにはこの処理を行いません。[Microsoftの説明](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleflushclipboard)を参照してください。

そのあとShellの非同期処理が残っていれば、50 msの非同期待機でDispatcherを動かし続けます。ウィンドウは閉じますが、プロセスはコピーなどの完了まで残ります。Shellの参照が解放されてから`Application.Shutdown`を呼び、同じSTA上でスレッド参照とカウンターを解放します。同期的な`Wait`や`Join`は使いません。

## 検証結果

利用者の許可を得て、専用フォルダー内のファイルだけで実画面を操作しました。コピー先を排他的に開けないことを確認し、コピー後の操作だけで成功扱いにしないようにしています。

| 操作 | サイズ | 結果 |
|---|---:|---|
| 同じタブ・同じフォルダー内のCtrl+V | 1 GiB | コピー中のウィンドウ応答と別ペインのタブ追加、完了後のSHA-256一致 |
| 同じタブ・同じフォルダー内の右クリック貼り付け | 1 GiB | コピー中のウィンドウ応答と別ペインのタブ追加、完了後のSHA-256一致 |
| 非選択タブ見出しへのCtrlドロップ | 4 GiB | コピー中のウィンドウ応答とタブ追加、コピー元の保持、完了後のSHA-256一致 |
| コピー中にウィンドウを閉じる | 4 GiB | コピー完了後のSHA-256一致とプロセスの正常終了 |

自動検証はCore 47/47、TabDrop 16/16、Sidebar 10/10、ShellOperations 3/3、Selection 7/7、QuickLook 8/8、Persistence 10/10、Navigation 4項目が成功しました。`ShellOperations`は実際のスレッド参照の取得・追加・解放と、終了待ちの間もDispatcherが応答することを確認します。

再検証は[Verify-FileOperationResponsiveness.ps1](../scripts/Verify-FileOperationResponsiveness.ps1)を使用します。起動ごとに`artifacts/file-operations-<ID>/`を作り、検証対象のプロセスとパスを確認してから操作します。マウス・キーを使用し、一時的にクリップボードを変更するため、実行前に利用者の許可を得てください。

```powershell
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Start -SizeMiB 1024
./scripts/Verify-FileOperationResponsiveness.ps1 -Step CtrlPaste
./scripts/Verify-FileOperationResponsiveness.ps1 -Step MenuPaste
./scripts/Verify-FileOperationResponsiveness.ps1 -Step TabDrop
./scripts/Verify-FileOperationResponsiveness.ps1 -Step Close
```

検証時点でコピーが完了してしまった場合は、サイズを増やして新しいセッションで再検証します。タブドロップを繰り返すと同名確認が出るため、コピー先の専用ファイルを片付けるか、新しいセッションを使用します。

今回はローカルの通常ファイルで確認しました。UNC・WSL・クラウド・仮想ファイルの転送、大容量の移動、コピー途中の取消や同名確認中の画面操作は未検証です。問題が残る経路では、専用STAでShellオブジェクトを作り直す方式や、元のOLEデータを直接受け取る方式を追加検討します。
