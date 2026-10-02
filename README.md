# claude-code-ctrl-enter-send

Claude デスクトップアプリ（Windows）の Code タブの入力欄で、Enter で送信されないようにし、Ctrl+Enter だけで送信する常駐プログラムです。

## 機能

Claude アプリが前面にあり、Code タブのプロンプト入力欄に焦点がある時だけ、次のように動きます。

| キー | 動き |
| --- | --- |
| Enter | 日本語の変換中は確定だけ。変換していない時は何も起きない |
| Shift+Enter | 改行 |
| Ctrl+Enter | 送信 |

- それ以外の場所（チャットなど他のタブ、ダイアログ、他のアプリ）では、キーをそのまま通します。
- 通知領域のアイコンをダブルクリックすると一時停止・再開します。右クリックのメニューから終了できます。
- Windows 標準の PowerShell だけで動きます。追加のインストールは不要です。

## 起動方法

`start.cmd` をダブルクリックします。窓は開かず、通知領域にアイコンが出ます。二重には起動しません。

止める時は、通知領域のアイコンを右クリックして「終了」を選ぶか、次を実行します。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\stop.ps1
```

## スタートアップ登録

ログイン時に自動で起動させるには、次を実行します。スタートアップのフォルダーにショートカット「Claude Code Ctrl+Enter 送信」が作られます。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\register-startup.ps1
```

登録を解除するには、スタートアップのフォルダー（エクスプローラーのアドレス欄に `shell:startup` と入力すると開きます）から、そのショートカットを削除します。

## アンインストール

設定やレジストリは変更しないので、削除するだけで済みます。

1. 通知領域のアイコンを右クリックして「終了」を選びます。
2. スタートアップに登録した場合は、`shell:startup` のショートカットを削除します。
3. このフォルダーを削除します。
