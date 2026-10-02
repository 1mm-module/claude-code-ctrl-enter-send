# ログイン時に常駐プログラムを窓なしで起動するショートカットをスタートアップに置く
$startup = [Environment]::GetFolderPath('Startup')
$lnk = Join-Path $startup 'Claude Code Ctrl+Enter 送信.lnk'
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($lnk)
$s.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$s.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $PSScriptRoot 'ctrl-enter-send.ps1') + '"'
$s.WorkingDirectory = $PSScriptRoot
$s.WindowStyle = 7
$s.Description = 'Claude Code の入力欄を Ctrl+Enter 送信にする'
$s.Save()
'登録しました：' + $lnk
