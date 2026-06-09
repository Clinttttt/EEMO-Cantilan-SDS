# clean-mobile.ps1
# 1. Kills build/adb processes holding the Android obj folder locked
# 2. Clears the locked obj folder
# 3. Re-sets up adb reverse for USB-connected phone (not needed for emulator — it uses 10.0.2.2)
# Run this whenever the mobile build fails with "Access to the path ... is denied".

$adb = "C:\Users\ASUS VIVOBOOK\AppData\Local\Android\Sdk\platform-tools\adb.exe"

Write-Host "Stopping build processes..." -ForegroundColor Yellow
Get-Process -Name "dotnet","MSBuild","java","adb","EEMOCantilanSDS.Mobile" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Clearing locked obj folder..." -ForegroundColor Yellow
Remove-Item -Recurse -Force "$PSScriptRoot\..\EEMOCantilanSDS.Mobile\obj\Debug\net10.0-android" -ErrorAction SilentlyContinue

Write-Host "Setting up adb reverse (USB phone only — skip if using emulator)..." -ForegroundColor Yellow
& $adb reverse --remove-all
& $adb reverse tcp:5117 tcp:5117
& $adb reverse --list

Write-Host "Done — rebuild in Visual Studio now." -ForegroundColor Green
