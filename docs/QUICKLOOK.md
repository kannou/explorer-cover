# QuickLook連携（段階5c）

実装・検証日: 2026-09-20

## 操作と設定

一覧でファイルを1つ選び、Spaceを押して離すとQuickLookで表示する。同じファイルに対してもう一度実行すると閉じる。キーを長押ししても、離すまで表示しない。待機中にタブ・場所・選択・フォーカスが変わった場合は要求を破棄する。

`quickView`は共通コマンドに登録し、`shortcuts.json`で変更・解除できる。対象は一覧のみ。パス入力、シェルの名前変更、IME変換中、外枠のボタン上ではプレビューキーとして処理しない。Spaceは`quickView`にのみ割り当て可能で、Ctrl+Spaceなどは入力との競合を避けるため禁止する。

起動設定は`%LOCALAPPDATA%\explorer-cover\quicklook.json`、または環境変数`EXPLORER_COVER_QUICKLOOK`で指定したJSONから読む。[サンプル](../config/quicklook.example.json)を用意した。

```json
{ "version": 1, "executablePath": null }
```

手動指定する場合は`executablePath`にQuickLook.exeの絶対パスを指定する。環境変数を含むパスも展開する。Bridge.exeや任意のコマンド行を指定するための設定ではない。不正なJSONは画面で通知して自動検出に戻す。有効な形式でも実行ファイルが存在しない場合は、プレビュー要求時にエラーを表示する。

## 実装

- `ExplorerHost`が`IFolderView.Items(SVGIO_SELECTION)`と`IShellItemArray`で実際の選択を取得する。単一のファイルシステム項目だけを受け付け、通常のフォルダーを除外する。ZIPのようにフォルダー属性とストリーム属性を併せ持つファイルは受け付ける。COM参照と文字列バッファは取得処理の終了時に解放する。
- `QuickLookClient`が同じWindowsユーザーの名前付きパイプへUTF-8の`Toggle`要求を送る。QuickLookの公開実装にある通信形式に合わせた独立したクライアントで、追加のBridgeバイナリは不要。
- まず起動済みのパイプへ250msを上限に接続する。未起動の場合は手動指定、Store版の登録、標準ディレクトリ（LocalAppData／Program Files／Program Files (x86)配下の`QuickLook\QuickLook.exe`）を使い、起動後は最大5秒待つ。Store版の識別子は`21090PaddyXu.QuickLook_egxr34yet59cg!Main`。
- ファイル存在確認、起動、接続はUIスレッドの外で実行する。同時要求は1件に制限し、送信直前にUI側で元の選択とフォーカスを再確認する。接続成立とキャンセルの競合で空切断しないよう、接続処理自体は250ms／起動後5秒の上限まで待ち、成立後にキャンセルを確認する。
- 接続後に要求を取り消す場合も、無操作の空行を送ってから切断する。QuickLook 4.5は何も受信せず切断されると`ReadLine()`の`null`を処理できず受信ループが停止する。成立した接続への一行送信は要求キャンセルから独立させ、2秒の上限を設ける。
- 起動や通信の失敗はペインのステータス欄に表示する。移動履歴の失敗として記録せず、タブ切替や次の操作で表示を更新する。
- この環境ではQuickLookの自動選択検出が独自のWPF外枠を対象にしないため、アプリ側の送信と二重トグルにならないことを実画面で確認した。QuickLookやシェル拡張の更新後は再確認する。

## 検証

Windows上のStore版QuickLook 4.5.0.0を使用した。Releaseビルドは警告・エラーなし。Coreテスト18件、独立した名前付きパイプによる通信テスト7件が成功（選択追従の追加後に通信テストを拡充）。

| 確認項目 | 結果 |
|---|---|
| 日本語・空白入りパスをSpaceで表示 | 成功 |
| 選択変更・矢印キーでプレビュー内容を更新 | 成功。ToggleではなくSwitchで更新 |
| プレビューを閉じた後の選択変更 | 成功。再表示なし。通信テストで未起動時に起動しないことも確認 |
| 長押し、キーリピート、二重トグル防止 | 成功。押下中は送信せず、離した後に1回だけ送信 |
| 押下中に別タブへ切替 | 成功。古い選択への要求を取消 |
| 同じキーで閉じて一覧操作を継続 | 成功。プレビューを強制的に前面化する操作なしで確認 |
| QuickLook側にフォーカスを移してEscapeで閉じる | 成功。元のアプリでCtrl+Lを実行可能 |
| パス入力・名前変更・日本語IME変換 | 成功。変換・確定でき、追加のプレビュー送信なし |
| 未選択・複数選択・通常のフォルダー | 成功。送信なし |
| フォルダー属性を持つZIPファイル | 成功。実際のZIPプレビュー表示を確認 |
| F8への変更・旧Spaceの解除 | 成功 |
| 起動先が存在しない場合 | 実画面でエラー表示と、その後のタブ操作を確認 |
| 未起動・起動失敗・接続タイムアウト・終了時中断 | 独立したテスト用パイプと起動処理の差し替えで成功 |

実画面検証は`scripts/Verify-QuickLook.ps1`。`-CustomKeys`でF8割り当て、`-Missing`で起動先が見つからない場合を検証する。各回に専用の設定・ファイルを`artifacts/quicklook-*`へ作成する。

再確認用ログ: 最終ビルドのSpace・IME・ZIPは`artifacts/quicklook-5509b010cf4d4c089d84250a8eb40b5c/app.log`、変更キー・ZIPは`artifacts/quicklook-8af20964b82f4a62ad30fd0bf6e730c5/app.log`。起動先不明時の初回検証は`artifacts/tabs-399c09f5b3e54b00b4c143d5dc3c9de1/app.log`。

## 試用時の確認と制限

選択追従を追加した後の実画面ログ: `artifacts/quicklook-41edfb9390894d84bbb9cffe335d450b/app.log`。選択変更、閉じた後の再表示防止、既存の開閉・キー入力・IME・ZIPを確認した。

- ユーザーの常駐QuickLookは停止していない。Store版の登録と実行ファイルは確認済みだが、実物を停止した状態からの自動起動は未検証。未起動時の分岐と接続再試行は通信テストで確認した。
- MSI版QuickLook 4.5.0.0は2026-09-23に実画面検証済み。ZIP版、ネットワーク・クラウドのファイル、特殊なシェル拡張は未検証。表示可能な形式・内容はQuickLookのプラグインに依存する。
- パイプには表示成功の応答がないため、送信完了と描画成功は区別する。起動後の5秒以内に準備が整わない場合は再度キーを押して試す。
- プレビュー開始後は、Shellの選択変更通知を受けてフォーカスのある一覧を確認する。連続通知は200 msの待機でまとめ、変化がない間は確認しない。単一ファイルの選択が変わった場合は`Switch`要求で表示中の内容だけを更新する。閉じたプレビューを再表示せず、QuickLookが未起動でも起動しない。未選択・複数選択・フォルダーでは直前の表示を維持する。名前変更・IME変換・パス入力中は更新しない。
- JSONの設定画面は段階7で追加する。次は試用結果を反映し、左サイドビュー（6a）へ進む。

## 2026-09-21: MSI版での受信停止対策

QuickLook 4.5.0の公開ソースに合わせた独立パイプの受信テストで、選択変更による空切断後に`NullReferenceException`が発生することを修正前に確認した。修正後は空行を無操作として処理し、同じ受信ループで次のプレビュー要求を受け付ける。接続後のキャンセルと選択確認の例外でも空行が届くことを含め、通信テスト8件が成功した。

常駐中のQuickLookへの操作や実画面テストは行っていない。既に受信処理が停止したQuickLookは、修正版のexplorer-coverへ更新後に再起動する必要がある。

## 2026-09-23: 選択監視の定期実行を削減

`ICommDlgBrowser.OnStateChange`の選択変更・改名・チェック状態変更を`ExplorerHost`から通知する。COMコールバック中には選択を読み直さず、Dispatcherへ渡す段階で重複をまとめる。非選択タブの通知はペインから転送せず、タブ切替・移動状態変更・フォーカス復帰・ウィンドウ再アクティブ化・編集終了を再評価の契機にする。[Microsoftの通知仕様](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icommdlgbrowser-onstatechange)

`SelectionRefreshScheduler`は通知後にだけタイマーを開始し、200 ms後に一回確認して停止する。送信中の通知は保留し、完了後に最新の選択を確認する。Toggleは実行中のSwitchを待ち、Toggle待機中は次のSwitchを開始しない。プレビュー未使用時は確認せず、終了時はタイマーと予約通知を破棄する。閉じた後の選択変更には従来どおりSwitchを使い、表示状態を推測してToggleを送ることはない。

画面を開かない`ExplorerCover.Selection.Tests`を追加し、無操作時の停止、連続通知の集約、非アクティブ時の抑止、送信中の最新選択の保持、Toggleとの直列化、同期再入、終了時の破棄を7件のテストで確認した。実画面用の`Verify-QuickLook.ps1`にも、表示中・終了後の無操作区間で診断ログの選択確認回数が増えない検証を追加した。

検証結果:

- Releaseビルドは警告0・エラー0。Core 38件、QuickLook通信8件、選択監視7件が成功。
- MSI版QuickLook 4.5.0.0と専用の検証アプリで`Verify-QuickLook.ps1`が成功。フォーカスを変えない選択変更と矢印キーへの追従、プレビュー表示中・終了後それぞれ1.2秒の無操作区間で選択確認回数が増えないことを確認した。
- 開閉、閉じた後の再表示防止、長押し、押下中のタブ切替、パス・名前変更欄のSpaceと日本語IME、未選択・複数選択・フォルダーの除外、ZIP表示、左右へのドラッグ移動も成功。Shellビューは生成3回・解放3回で、検証アプリは終了コード0。
- `ExplorerCover.Navigation.Tests`の4項目も成功。古い移動結果の破棄、タイムアウト後の再試行、不正入力時の取消、タブ破棄後の通知抑止を確認した。
- 実画面ログ: `artifacts/quicklook-7ed13ececc284df7b39ec5e859350e6b/app.log`。実行結果: `artifacts/performance-review/quicklook-ui.txt`、`artifacts/performance-review/navigation-ui.txt`。

## 参考資料

- [QuickLook公式の連携案内](https://github.com/QL-Win/QuickLook/wiki/Develop,-build-and-integrate)
- [QuickLookの通信プロトコル](https://github.com/QL-Win/QuickLook/blob/master/QuickLook/PipeServerManager.cs)
- [QuickLookのキー処理](https://github.com/QL-Win/QuickLook/blob/master/QuickLook/KeystrokeDispatcher.cs)
- [IFolderView::Items](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-items)
- [SFGAOの属性定義](https://learn.microsoft.com/en-us/windows/win32/shell/sfgao)
