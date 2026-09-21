param(
 [ValidateSet('Start','Inspect','Menu','Delete','KeyDelete','Confirm','Cancel','Down','Close')][string]$Step = 'Inspect',
 [ValidateSet('left','right')][string]$Side = 'left'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DeleteFocusInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 public static void Check(int pid) { uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current); if(current!=pid) throw new InvalidOperationException("検証対象が前面にありません。"); }
 public static void Click(int pid,int x,int y,bool right) { Check(pid); SetCursorPos(x,y); mouse_event(right?8u:2u,0,0,0,UIntPtr.Zero); mouse_event(right?16u:4u,0,0,0,UIntPtr.Zero); }
 public static void Key(int pid,byte key) { Check(pid); keybd_event(key,0,0,UIntPtr.Zero); keybd_event(key,0,2,UIntPtr.Zero); }
}
'@
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$record = Join-Path $root 'artifacts\delete-focus-session.json'
if ($Step -eq 'Start') {
 $trial = Join-Path $root ('artifacts\delete-focus-' + [Guid]::NewGuid().ToString('N'))
 $left = Join-Path $trial 'left'; $right = Join-Path $trial 'right'
 New-Item -ItemType Directory -Path $left,$right | Out-Null
 $targetDirectory = if ($Side -eq 'left') { $left } else { $right }
 foreach ($name in @('01-delete-me.txt','02-keep.txt','03-keep.txt')) { Set-Content -LiteralPath (Join-Path $targetDirectory $name) -Value 'フォーカス検証用ダミーファイル' }
 $env:EXPLORER_COVER_LOG = Join-Path $trial 'app.log'
 $env:EXPLORER_COVER_SETTINGS = Join-Path $trial 'input.json'
 Set-Content -LiteralPath $env:EXPLORER_COVER_SETTINGS -Value '{"version":1,"shortcuts":{"version":1,"bindings":{}},"mouse":{"version":1}}'
 $env:EXPLORER_COVER_STATE = Join-Path $trial 'workspace.json'
 $dll = Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer-cover.dll'
 $app = Start-Process -FilePath (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Normal -PassThru
 @{ pid=$app.Id; trial=$trial; file=(Join-Path $targetDirectory '01-delete-me.txt') } | ConvertTo-Json | Set-Content -LiteralPath $record
 Start-Sleep -Seconds 3
}
$session = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$windows = @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $session.pid })
$window = $windows | Where-Object { $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
if (!$window) { throw '専用の検証ウィンドウが見つかりません。' }
function Elements { @($window.FindAll($scope,$all)) }
function Click-Element($element,[bool]$right=$false) {
 $point=$element.GetClickablePoint()
 [DeleteFocusInput]::Click($session.pid,[int]$point.X,[int]$point.Y,$right)
 Start-Sleep -Milliseconds 450
}
if ($Step -eq 'Start') { [DeleteFocusInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null }
if ($Step -in @('Menu','KeyDelete')) {
 $item = Elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '01-delete-me.txt' } | Select-Object -First 1
 if (!$item -or !(Test-Path -LiteralPath $session.file)) { throw '検証用ファイルがありません。' }
 Click-Element $item ($Step -eq 'Menu')
 if ($Step -eq 'KeyDelete') { [DeleteFocusInput]::Key($session.pid,0x2E); Start-Sleep -Milliseconds 450 }
}
if ($Step -eq 'Delete') {
 $menus = @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $session.pid })
 $delete = $menus | ForEach-Object { $_.FindAll($scope,$all) } | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -match '^削除' } | Select-Object -First 1
 if (!$delete) { throw '削除メニューが見つかりません。' }
 Click-Element $delete
}
if ($Step -in @('Confirm','Cancel')) {
 $dialog = $windows | Where-Object { $_.Current.Name -match '削除' } | Select-Object -First 1
 if (!$dialog) { $dialog = Elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -match '削除' } | Select-Object -First 1 }
 if (!$dialog) { throw '削除確認画面がありません。' }
 $names = @($dialog.FindAll($scope,$all) | ForEach-Object { $_.Current.Name })
 if (!(($names -join ' ') -match '01-delete-me')) { throw '削除対象が検証ファイルと一致しません。' }
 [DeleteFocusInput]::Key($session.pid, $(if ($Step -eq 'Confirm') { 0x0D } else { 0x1B }))
 Start-Sleep -Milliseconds 700
 if ($Step -eq 'Confirm' -and (Test-Path -LiteralPath $session.file)) { throw '検証ファイルが削除されていません。' }
 if ($Step -eq 'Cancel' -and !(Test-Path -LiteralPath $session.file)) { throw 'キャンセルしたファイルが消えています。' }
}
if ($Step -eq 'Down') { [DeleteFocusInput]::Key($session.pid,0x28); Start-Sleep -Milliseconds 400 }
if ($Step -eq 'Close') { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); exit }
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Write-Output ('FOCUS: ' + $focused.Current.ControlType.ProgrammaticName + ' / ' + $focused.Current.Name + ' / ' + $focused.Current.AutomationId)
@{ step=$Step; type=$focused.Current.ControlType.ProgrammaticName; name=$focused.Current.Name; id=$focused.Current.AutomationId } | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $session.trial 'focus.jsonl')
if ($Step -in @('Confirm','Cancel','Down')) {
 $expected = if ($Step -eq 'Cancel') { '01-delete-me.txt' } elseif ($Step -eq 'Confirm') { '02-keep.txt' } elseif (Test-Path -LiteralPath $session.file) { '02-keep.txt' } else { '03-keep.txt' }
 if ($focused.Current.ProcessId -ne $session.pid -or $focused.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem -or $focused.Current.Name -ne $expected) { throw "一覧の期待した項目にフォーカスがありません: $expected" }
}
if ($Step -in @('Inspect','Delete','Menu')) {
 $windows | ForEach-Object { 'WINDOW: ' + $_.Current.Name; $_.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -in @([System.Windows.Automation.ControlType]::MenuItem,[System.Windows.Automation.ControlType]::Button) } | ForEach-Object { $_.Current.ControlType.ProgrammaticName + ': ' + $_.Current.Name } }
}
Write-Output ('記録: ' + $session.trial)
