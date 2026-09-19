param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class MenuInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$all = [System.Windows.Automation.Condition]::TrueCondition
$scope = [System.Windows.Automation.TreeScope]::Descendants
$root = [System.Windows.Automation.AutomationElement]::RootElement
$window = $root.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'Explorer Alt*' } | Select-Object -First 1
[MenuInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
function Wait-Until([scriptblock]$Check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(10)
 do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw '操作結果の待機がタイムアウトしました。'
}
function Find-Item([string]$name) { $window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $name } | Select-Object -First 1 }
function Open-Menu([int]$x,[int]$y) {
 [MenuInput]::SetCursorPos($x,$y) | Out-Null
 [MenuInput]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
 [MenuInput]::mouse_event(16,0,0,0,[UIntPtr]::Zero)
 Wait-Until { @($root.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu -and $_.Current.ProcessId -eq $ProcessId }).Count -gt 0 }
}
$testRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\trial'))
$name = '切り取り-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt'
$source = Join-Path $testRoot "left\$name"; $target = Join-Path $testRoot "right\$name"
Set-Content -LiteralPath $source -Value '切り取りの検証'
Wait-Until { $null -ne (Find-Item $name) }
$item = Find-Item $name
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
[System.Windows.Forms.SendKeys]::SendWait('^x')
[System.Windows.Forms.SendKeys]::SendWait('{F6}')
[System.Windows.Forms.SendKeys]::SendWait('^v')
Wait-Until { (Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $source) }
'PASS: Ctrl+X / F6 / Ctrl+Vによる左右間の移動'
$folder = Find-Item '日本語フォルダー'
$point = $folder.GetClickablePoint()
Open-Menu ([int]$point.X) ([int]$point.Y)
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
'PASS: フォルダーの右クリックメニュー'
$lists = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List })
$rect = $lists[0].Current.BoundingRectangle
Open-Menu ([int]($rect.X+$rect.Width/2)) ([int]($rect.Bottom-65))
$menu = $root.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu -and $_.Current.ProcessId -eq $ProcessId } | Select-Object -First 1
$refresh = $menu.FindAll($scope,$all) | Where-Object { $_.Current.Name -like '最新の情報に更新*' } | Select-Object -First 1
if (!$refresh) { [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); throw '背景メニューの更新項目が見つかりません。' }
$refresh.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
'PASS: 一覧背景の右クリックと「最新の情報に更新」の実行'
