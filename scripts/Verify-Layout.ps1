param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class LayoutInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$all = [System.Windows.Automation.Condition]::TrueCondition
$scope = [System.Windows.Automation.TreeScope]::Descendants
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
[LayoutInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
$lists = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List })
$before = $lists[0].Current.BoundingRectangle.Width
$thumb = $window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Thumb } | Select-Object -First 1
$point = $thumb.GetClickablePoint()
[LayoutInput]::SetCursorPos([int]$point.X,[int]$point.Y) | Out-Null
[LayoutInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
try {
 for ($i=1;$i -le 10;$i++) { [LayoutInput]::SetCursorPos([int]($point.X+10*$i),[int]$point.Y) | Out-Null; Start-Sleep -Milliseconds 30 }
} finally { [LayoutInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
Start-Sleep -Milliseconds 300
if ([Math]::Abs($lists[0].Current.BoundingRectangle.Width-$before) -lt 50) { throw 'ペイン幅が変わりません。' }
'PASS: 境界のドラッグとネイティブ一覧幅の追従'
$pattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
$pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
Start-Sleep -Milliseconds 300
if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) { throw '最大化できません。' }
$pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
Start-Sleep -Milliseconds 500
'PASS: 最大化と復元'
$rect = $window.Current.BoundingRectangle
$bmp = [System.Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
try {
 $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bmp.Size)
 $bmp.Save([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\prototype.png')))
} finally { $graphics.Dispose(); $bmp.Dispose() }
'PASS: 試作ウィンドウのスクリーンショット保存'
