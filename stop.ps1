# 動いている Ctrl+Enter 送信の常駐プログラムを止める
foreach ($p in (Get-CimInstance Win32_Process -Filter "Name='powershell.exe'")) {
    if ($p.CommandLine -and $p.CommandLine.Contains('ctrl-enter-send.ps1')) {
        Stop-Process -Id $p.ProcessId
        '停止しました：' + $p.ProcessId
    }
}
