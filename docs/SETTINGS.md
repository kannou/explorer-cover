# 設定画面（段階7）

## 使い方

左下の⚙から開く。操作名と適用範囲を見ながらキーを入力する。例: `Ctrl+T`、複数なら`F8 / Ctrl+T`。空欄は割り当て解除。「初期値に戻す」は画面内の全設定を初期値へ戻す。「保存」で検証・保存してから反映し、「キャンセル」または閉じる操作では破棄する。

`[`・`]`、`Ctrl+[`・`Ctrl+]`なども指定できる。Ctrl・Altを付けない括弧キーはパス欄では無効となり、文字入力を優先する。名前変更中・IME変換中も既存の入力保護を維持する。

下へスクロールすると、タブを閉じるマウスボタンとQuickLookの起動先を変更できる。起動先は実行ファイルの絶対パスを入力し、空欄は自動検出。存在確認はプレビュー実行時に行う。

キー・マウスは保存直後に反映される。QuickLookの起動先は次のプレビュー要求から使用する。既に動作しているQuickLookは終了・再起動しない。設定画面を開いている間、背後のファイル操作は実行しない。

## 保存と既存設定の扱い

- 通常は`%LOCALAPPDATA%\explorer-cover\input.json`へ保存する。形式はversion 1で、`shortcuts`・`mouse`・`quickLook`をまとめる。
- 作業状態の`workspace.json`とは別。書き込みを完了した一時ファイルから置換し、前の内容を`.bak`に残す。失敗時は実行中の設定を変更せず、エラーと編集中の内容を画面に残す。
- `input.json`がなければ既存の`shortcuts.json`・`mouse.json`・`quicklook.json`を読む。画面で保存すると統合設定へ移行し、以降はそちらを優先する。旧ファイルは変更しない。
- 統合設定を読めなければ警告し、旧形式へフォールバックする。明示的な保存時には統合設定を置換し、元の内容をバックアップする。
- `EXPLORER_COVER_SETTINGS`で統合設定の保存先を指定できる。未指定で従来の`EXPLORER_COVER_SHORTCUTS`、`EXPLORER_COVER_MOUSE`、`EXPLORER_COVER_QUICKLOOK`のいずれかを指定した場合は、この順で最初の指定パスに`.input.json`を付けた場所を使う。検証用設定と通常設定を分離するための規則。
- 複数起動で設定を保存した場合は最後の保存を優先する。他の起動中ウィンドウには自動反映しない。

## 対応範囲

タブ・履歴・ペイン・パス欄・QuickLookに加え、一覧でのコピー・切り取り・貼り付け・削除・名前変更のキーを変更できる。これらを変更・解除した場合、元のキーを一覧のShellへ流さない。文字編集中のCtrl+C／X／V等は通常どおり使える。

コピー・切り取り・貼り付け・削除はShellの標準verbを実行する。削除確認やファイル操作のUIもShellに従う。名前変更はShellビューの名前編集を開始し、確定・IME処理はShellに委ねる。

`Ctrl+PageUp`・`Ctrl+PageDown`も割り当て可能。例として「前のタブ」「次のタブ」に使用できる。初期割り当ては変更しない。

同じ範囲の重複や、文字入力を奪うキーは保存できない。Ctrl+C／X／V・Delete・F2は対応する元の操作の初期キーとしてのみ許可する。F5・F10、Shift+Delete、右クリックメニュー内・シェル拡張内部のキーは変更対象外。SpaceはQuickLook専用。マウスの左ボタンは選択・ドラッグ用として維持する。

## 検証（2026-09-21）

- Releaseビルド: 警告・エラーなし。Coreテスト33件成功。
- 追加修正: 設定ボタンの左下配置、単独 `[` と `Ctrl+]` での履歴移動、パス編集中の単独括弧キーの保護を実画面で確認。設定の保存・再起動を含む検証も成功。記録: `artifacts/settings-e969a640a6874e188f6757ab1b5a0238`。
- `scripts/Verify-Settings.ps1`: 競合・入力保護、キャンセル、保存後のキー／マウス反映と旧割り当て解除、再起動、初期値復帰、割り当て解除、QuickLook起動先の検証・保存を確認。
- 保存先をロックした場合のエラー表示、原本と実行中の設定の保持を確認。
- 一時ファイルで変更後のキーによるコピー・貼り付け・名前変更・切り取り・削除を確認。削除確認は検証ファイルの名前を確認して承認する。
- 記録: `artifacts/settings-d887e2ebb1c24e66b2f06ae1c2469370`。設定画面の`settings.png`を目視確認。
- `scripts/Verify-Tabs.ps1`: 履歴・選択・スクロール保持、パス欄とShell名前変更欄での日本語IME、左右間のドラッグ、ビュー解放が成功。記録: `artifacts/tabs-2b785db3e1784463b2d066c81c25db57`。
- `scripts/Verify-QuickLook.ps1`: プレビュー・選択追従・再表示抑制・文字編集保護・日本語IME・ZIP表示が成功。記録: `artifacts/quicklook-53f71293997c4f30a9dba0553ce41117`。検証スクリプトの全要素列挙で一覧更新中に参照が失効したため、対象名の検索と一時的な失効時の再試行へ修正して確認した。

次は実際にキー・マウスの設定を変更して試用し、入力方法や画面配置の改善点を整理する。

## 参照API

- [IShellView::GetItemObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-getitemobject)
- [IContextMenu::InvokeCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-invokecommand)
- [SVSI_EDIT（名前編集の開始）](https://learn.microsoft.com/ja-jp/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif)
