# タブのマウス操作（段階5b-2）

実装日: 2026-09-20

## 操作

- 左ドラッグ: 同じペイン内で並べ替え。移動距離がしきい値を超えると挿入位置を表示し、バー内で離すと確定する。バー端に保持すると横スクロールする。
- Escape、バー外でのドロップ、キャプチャ喪失、ウィンドウ非アクティブ化: 並べ替えを取り消す。ドラッグ開始時に選択したタブは維持する。
- 中クリック（初期値）: クリックした見出しのタブを閉じる。非選択タブを閉じても表示中のタブを維持する。最後の1タブは閉じない。
- バーの空白を左ダブルクリック: そのペインの起動時の場所を新しいタブで開く。見出し、操作ボタン、スクロールバーは対象外。

閉じるボタンは`mouse.json`で`middle`、`right`、`xButton1`、`xButton2`、`none`に変更できる。読み込み先・指定例は[README](../README.md)を参照。設定画面からの変更・保存は段階7で追加する。

## 構成

- `PaneState.MoveTab`: 同じタブオブジェクトをコレクション内で移動する。選択中タブ、履歴、編集中のパスを変更しない。
- `TabStrip`: 見出しの並びとマウスジェスチャーを担当する。マウスキャプチャでバー内のドラッグを扱い、シェルのファイルDrag & Dropへ渡さない。ドロップ成功時だけモデルを更新する。
- `BrowserPane`: 既存のシェルビューを保持したまま、見出しの順序だけを反映する。マウスで指定したタブの終了とキー・×ボタンの終了は同じ処理を使う。
- 追加・並べ替え直後の選択見出しへのスクロールは、レイアウトで位置が確定してから行うよう統一した。端スクロールのテストでは、ウィンドウ枠を基準にした入力座標がタブバー外に出ていたため、バー内への入力に修正した。
- `MouseSettings`: WPFに依存しない設定の解析と検証。左ボタン、不明な値、重複、不明なプロパティ、形式の誤りを拒否する。
- `MouseSettingsFile`: キー設定・作業状態と分離して起動時に読み込む。不正・読み込み失敗時は初期値に戻して通知する。既存設定を書き換えない。

## 検証状況

- Releaseビルド: 成功、警告0・エラー0。
- Coreテスト: 16/16成功。並べ替えによるID・選択・編集中のパス・履歴の保持、範囲外・別ペインのタブの拒否、非選択タブの終了、設定の全選択肢・不正値を含む。
- 実画面: ロック解除後に検証を再開し、並べ替え・取消・空白からの追加・各ボタン設定・旧ボタン解除・無効化・不正設定時の初期値復帰・最後のタブ保護を確認した。全検証アプリの正常終了とビューの生成／解放数の一致も確認した。再実行手順は以下のとおり。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-TabMouse.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-TabMouse.ps1 -Profile right
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-TabMouse.ps1 -Profile xButton1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-TabMouse.ps1 -Profile xButton2
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-TabMouse.ps1 -Profile none
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-TabMouse.ps1 -Profile invalid
```

検証スクリプトは専用フォルダーとアプリを作り、前面のプロセスが対象と一致するときだけ入力する。終了時は開いた検証アプリだけを閉じる。並べ替えの先頭／末尾移動、Escape・バー外ドロップ、キー切替順と履歴、空白／見出しのクリック判定、設定変更・旧ボタン解除・無効化・最後のタブ保護・解放数を確認する。

追加の実画面確認も成功: 9タブを開いた状態でのバー端の自動スクロール、最小化によるフォーカス喪失時の取消、押下後に見出し外へ移動した場合の終了取消、並べ替え後の一覧の選択・スクロール位置保持。追加項目のみを再実行する場合は`-AdvancedOnly`を指定する。

成功記録（各フォルダーの`app.log`）:

| 検証 | artifacts配下のフォルダー |
|---|---|
| 中クリック・基本ドラッグ・空白からの追加 | `mouse-867052c4b03b4e0fb6863f1157306c2a` |
| 右ボタン | `mouse-84566b111fb44b12b8d9743609d96c70` |
| サイドボタン1 | `mouse-d8755b70171e4b98b64101865f6ed646` |
| サイドボタン2 | `mouse-f0ca017c536048c9991b9dbc2352d3c7` |
| 無効化 | `mouse-d60ca82637af457fa8ff5d848ca86854` |
| 不正設定 | `mouse-2cae6a1e85444632b327e937d3d7bc86` |
| 端スクロール・フォーカス喪失・一覧状態の保持 | `mouse-18bb89da6c0e41af9ed36c0bea9c5c35` |

スクロール後のUI Automationの一覧参照が無効になるケースは、検証側で項目を再取得して対応した。実画面検証のためにユーザーのマウス設定・常駐キー設定を変更していない。

既存の`Verify-Tabs.ps1`による回帰検証も成功。タブ・履歴・選択／スクロール保持、日本語IMEのパス入力と名前変更、左右間のドラッグ移動、ビュー解放を確認した。記録: `artifacts/tabs-f8645600d08040f09a8b3d22c34a8e90/app.log`。
