param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellTestInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$root = [System.Windows.Automation.AutomationElement]::RootElement
$window = $root.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'Explorer Alt*' } | Select-Object -First 1
if (!$window) { throw '試作のウィンドウが見つかりません。' }
[ShellTestInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
$testRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\trial'))
$addresses = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ClassName -eq 'TextBox' })
if ($addresses[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne (Join-Path $testRoot 'left') -or $addresses[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne (Join-Path $testRoot 'right')) { throw '検証フォルダー以外では実行できません。' }
function Find-Item([string]$Name) { $window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $Name } | Select-Object -First 1 }
function Wait-Until([scriptblock]$Check) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw '操作結果の待機がタイムアウトしました。'
}
$name = '操作検証-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt'
$original = Join-Path $testRoot "left\$name"
Set-Content -LiteralPath $original -Value '名前変更、コピー、削除の検証用'
Wait-Until { $null -ne (Find-Item $name) }
$item = Find-Item $name
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$item.SetFocus()
[System.Windows.Forms.SendKeys]::SendWait('{F2}')
Start-Sleep -Milliseconds 300
# 拡張子も含めて全選択し、専用ファイルだけを名前変更する。
$renamedName = $name.Replace('操作検証','変更済み')
[System.Windows.Forms.SendKeys]::SendWait('^a')
[System.Windows.Forms.SendKeys]::SendWait($renamedName)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$renamed = Join-Path $testRoot "left\$renamedName"
Wait-Until { Test-Path -LiteralPath $renamed }
'PASS: F2による日本語ファイル名への変更'
$item = Find-Item $renamedName
$point = $item.GetClickablePoint()
[ShellTestInput]::SetCursorPos([int]$point.X,[int]$point.Y) | Out-Null
[ShellTestInput]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
[ShellTestInput]::mouse_event(16,0,0,0,[UIntPtr]::Zero)
Wait-Until { @($root.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu -and $_.Current.ProcessId -eq $ProcessId }).Count -gt 0 }
$menus = @($root.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu -and $_.Current.ProcessId -eq $ProcessId })
$menus | ForEach-Object { $_.FindAll($scope,$all) | ForEach-Object { 'MENU: ' + $_.Current.Name } }
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
'PASS: ファイルのシェル右クリックメニュー表示'
$lists = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List })
$targetRect = $lists[1].Current.BoundingRectangle
$targetX = [int]($targetRect.X + $targetRect.Width / 2)
$targetY = [int]($targetRect.Y + $targetRect.Height - 80)
$point = (Find-Item $renamedName).GetClickablePoint()
[ShellTestInput]::SetCursorPos([int]$point.X,[int]$point.Y) | Out-Null
[ShellTestInput]::keybd_event(0x11,0,0,[UIntPtr]::Zero)
try {
    [ShellTestInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    for ($step=1; $step -le 20; $step++) {
        [ShellTestInput]::SetCursorPos([int]($point.X + ($targetX-$point.X)*$step/20),[int]($point.Y + ($targetY-$point.Y)*$step/20)) | Out-Null
        Start-Sleep -Milliseconds 30
    }
    Start-Sleep -Milliseconds 300
} finally {
    [ShellTestInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    [ShellTestInput]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
}
Wait-Until { Test-Path -LiteralPath (Join-Path $testRoot "right\$renamedName") }
if (!(Test-Path -LiteralPath $renamed)) { throw 'Ctrlドラッグなのに移動されました。' }
'PASS: 左右間のCtrlドラッグによるコピー'
$item = Find-Item $renamedName
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$item.SetFocus()
[System.Windows.Forms.SendKeys]::SendWait('{DELETE}')
Start-Sleep -Milliseconds 500
$dialog = $window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -eq 'ファイルの削除' } | Select-Object -First 1
if ($dialog) {
    $elements = @($dialog.FindAll($scope,$all))
    if (!($elements | Where-Object { $_.Current.Name -eq $renamedName })) { throw '削除ダイアログの対象が検証用ファイルと一致しません。' }
    $yes = $elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -eq 'はい(Y)' } | Select-Object -First 1
    $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
Wait-Until { !(Test-Path -LiteralPath $renamed) }
'PASS: Deleteによる検証ファイルの削除（Shiftなし）'
