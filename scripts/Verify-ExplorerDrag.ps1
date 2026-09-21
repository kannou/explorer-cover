param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class DragInput {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
}
'@
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
$testRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\trial'))
$shell = New-Object -ComObject Shell.Application
$explorerCom = $shell.Windows() | Where-Object { $_.LocationURL -eq ([Uri](Join-Path $testRoot 'explorer')).AbsoluteUri } | Select-Object -First 1
if (!$explorerCom) { throw '検証専用フォルダーのExplorerが見つかりません。' }
$explorer = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$explorerCom.HWND)
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
if ($area.Width -lt 1400) { throw '横に並べるには1400px以上必要です。' }
[DragInput]::ShowWindow([IntPtr]$explorerCom.HWND,1) | Out-Null
[DragInput]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,$area.X,$area.Y,720,640,0) | Out-Null
[DragInput]::SetWindowPos([IntPtr]$explorerCom.HWND,[IntPtr]::Zero,($area.X+730),$area.Y,660,640,0) | Out-Null
Start-Sleep -Milliseconds 500
function Wait-Until([scriptblock]$Check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(10)
 do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw 'ドラッグ結果の待機がタイムアウトしました。'
}
function Find-Item($parent,[string]$name) { $parent.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $name } | Select-Object -First 1 }
function Drag-To($item,$target,[bool]$copy) {
 $from = $item.GetClickablePoint()
 $rect = $target.Current.BoundingRectangle
 $x = [int]($rect.X+$rect.Width/2); $y = [int]($rect.Y+$rect.Height-65)
 [DragInput]::SetCursorPos([int]$from.X,[int]$from.Y) | Out-Null
 if ($copy) { [DragInput]::keybd_event(0x11,0,0,[UIntPtr]::Zero) }
 try {
  [DragInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  for ($i=1;$i -le 30;$i++) {
   [DragInput]::SetCursorPos([int]($from.X+($x-$from.X)*$i/30),[int]($from.Y+($y-$from.Y)*$i/30)) | Out-Null
   Start-Sleep -Milliseconds 25
  }
  Start-Sleep -Milliseconds 300
 } finally {
  [DragInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  if ($copy) { [DragInput]::keybd_event(0x11,0,2,[UIntPtr]::Zero) }
 }
}
$name = 'Explorer受渡し-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt'
Set-Content -LiteralPath (Join-Path $testRoot "left\$name") -Value 'Explorerとのドラッグ検証'
Wait-Until { $null -ne (Find-Item $window $name) }
$target = $explorer.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and $_.Current.Name -eq '項目ビュー' } | Select-Object -First 1
Drag-To (Find-Item $window $name) $target $true
Wait-Until { Test-Path -LiteralPath (Join-Path $testRoot "explorer\$name") }
'PASS: 試作からExplorerへCtrlドラッグでコピー'
Wait-Until { $null -ne (Find-Item $explorer $name) }
$lists = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List })
Drag-To (Find-Item $explorer $name) $lists[1] $false
Wait-Until { (Test-Path -LiteralPath (Join-Path $testRoot "right\$name")) -and !(Test-Path -LiteralPath (Join-Path $testRoot "explorer\$name")) }
'PASS: Explorerから試作へ通常ドラッグで移動（同一ドライブ）'
$explorerCom.Quit()
[DragInput]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,($area.X+50),($area.Y+50),1200,740,0) | Out-Null
