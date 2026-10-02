@echo off
rem Ctrl+Enter 送信の常駐プログラムを窓なしで起動する（二重起動はしない）
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0ctrl-enter-send.ps1"
