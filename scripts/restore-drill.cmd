@echo off
REM Runs the restore drill from an ordinary Command Prompt.
REM
REM WHY THIS EXISTS
REM   cmd.exe cannot execute a .ps1 file directly - it hands it to a file association and returns,
REM   silently, having done nothing. That is a confusing way to learn a procedure, so this wrapper
REM   forwards to PowerShell with the flags the script needs and passes your arguments through.
REM
REM USAGE (from cmd, in the repository root)
REM   scripts\restore-drill.cmd
REM   scripts\restore-drill.cmd -KeepContainer
REM   scripts\restore-drill.cmd -RunId 31295284398
REM   scripts\restore-drill.cmd -DumpPath C:\somewhere\backup.dump
REM
REM From PowerShell you can call the .ps1 directly instead:
REM   .\scripts\restore-drill.ps1 -KeepContainer

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0restore-drill.ps1" %*
exit /b %ERRORLEVEL%
