# Claude デスクトップアプリの入力欄を Ctrl+Enter 送信にする常駐プログラムの起動スクリプト
param(
    # 対象を Claude アプリ以外にする時だけ指定する（自動テスト用。実行ファイルのパスの末尾）
    [string[]]$TargetSuffix,
    # 動作記録の保存先（自動テスト用）
    [string]$LogPath,
    # 焦点の判定をせず、対象アプリのすべての Enter を置き換える（自動テスト用）
    [switch]$AllEnter,
    [string]$MutexName = 'Local\ClaudeCtrlEnterSend'
)
$ErrorActionPreference = 'Stop'

# 二重起動を防ぐ
$mutex = New-Object System.Threading.Mutex($false, $MutexName)
try {
    if (-not $mutex.WaitOne(0)) {
        exit 0
    }
}
catch [System.Threading.AbandonedMutexException] {
}

$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CtrlEnterSend.cs') -Raw -Encoding UTF8
Add-Type -TypeDefinition $source -ReferencedAssemblies System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes, WindowsBase
if ($AllEnter) {
    [CtrlEnterSend]::RequireCodePrompt = $false
}
if ($TargetSuffix) {
    [CtrlEnterSend]::OverrideSuffixes = $TargetSuffix
}
if ($LogPath) {
    [CtrlEnterSend]::LogPath = $LogPath
    [CtrlEnterSend]::VerboseLog = $true
}
[CtrlEnterSend]::Run()
