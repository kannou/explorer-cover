param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class FinalInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
'@
$all = [System.Windows.Automation.Condition]::TrueCondition
$scope = [System.Windows.Automation.TreeScope]::Descendants
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
[FinalInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
$addresses = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ClassName -eq 'TextBox' })
$left = $addresses[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$right = $addresses[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$artifacts = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts')) + '\'
if (!$left.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase) -or !$right.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase)) { throw '検証用フォルダー以外では実行できません。' }
function Wait-Until([scriptblock]$Check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(10)
 do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw '操作結果の待機がタイムアウトしました。'
}
function Find-Item([string]$name) {
 # Shell一覧の更新中に消える子要素を、全件列挙で参照しない。
 try {
  $condition=[System.Windows.Automation.AndCondition]::new(
   [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem),
   [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))
  $window.FindFirst($scope,$condition)
 } catch [System.Windows.Automation.ElementNotAvailableException] { return $null }
}
$id = [Guid]::NewGuid().ToString('N').Substring(0,8)
$name = "IME-$id.txt"
Set-Content -LiteralPath (Join-Path $left $name) -Value 'IMEとドラッグ移動の検証'
Wait-Until { $null -ne (Find-Item $name) }
$item = Find-Item $name
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
[System.Windows.Forms.SendKeys]::SendWait('{F2}')
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('^a')
[FinalInput]::keybd_event(0x19,0,0,[UIntPtr]::Zero); [FinalInput]::keybd_event(0x19,0,2,[UIntPtr]::Zero)
try {
 [System.Windows.Forms.SendKeys]::SendWait('nihongo')
 [System.Windows.Forms.SendKeys]::SendWait(' ')
 [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
} finally {
 [FinalInput]::keybd_event(0x19,0,0,[UIntPtr]::Zero); [FinalInput]::keybd_event(0x19,0,2,[UIntPtr]::Zero)
}
[System.Windows.Forms.SendKeys]::SendWait("-ime-$id.txt")
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$renamed = "日本語-ime-$id.txt"
Wait-Until { Test-Path -LiteralPath (Join-Path $left $renamed) }
'PASS: シェルの名前変更欄でIME入力・変換・確定'
Wait-Until { $null -ne (Find-Item $renamed) }
$point = (Find-Item $renamed).GetClickablePoint()
$lists = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List })
$rect = $lists[1].Current.BoundingRectangle
$x = [int]($rect.X+$rect.Width/2); $y = [int]($rect.Bottom-65)
[FinalInput]::SetCursorPos([int]$point.X,[int]$point.Y) | Out-Null
[FinalInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
try {
 for ($i=1;$i -le 20;$i++) { [FinalInput]::SetCursorPos([int]($point.X+($x-$point.X)*$i/20),[int]($point.Y+($y-$point.Y)*$i/20)) | Out-Null; Start-Sleep -Milliseconds 30 }
 Start-Sleep -Milliseconds 300
} finally { [FinalInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
Wait-Until { (Test-Path -LiteralPath (Join-Path $right $renamed)) -and !(Test-Path -LiteralPath (Join-Path $left $renamed)) }
'PASS: 左右間の通常ドラッグによる移動（同一ドライブ）'
