param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class ClipboardInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$all = [System.Windows.Automation.Condition]::TrueCondition
$scope = [System.Windows.Automation.TreeScope]::Descendants
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'Explorer Alt*' } | Select-Object -First 1
$addresses = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ClassName -eq 'TextBox' })
$left = $addresses[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$right = $addresses[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$artifacts = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts')) + '\'
if (!$left.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase) -or !$right.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase)) { throw '検証用フォルダー以外では実行できません。' }
function Wait-Until([scriptblock]$Check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(10)
 do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw 'クリップボード操作の待機がタイムアウトしました。'
}
function Find-Item($parent,[string]$name) { $parent.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $name } | Select-Object -First 1 }
$external = Join-Path $artifacts ('clipboard-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $external | Out-Null
Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList @('/n,',('"'+$external+'"')) -WindowStyle Hidden
$shell = New-Object -ComObject Shell.Application
Wait-Until { @($shell.Windows() | Where-Object { $_.LocationURL -eq ([Uri]$external).AbsoluteUri }).Count -eq 1 }
$explorerCom = $shell.Windows() | Where-Object { $_.LocationURL -eq ([Uri]$external).AbsoluteUri } | Select-Object -First 1
try {
 [ClipboardInput]::ShowWindow([IntPtr]$explorerCom.HWND,1) | Out-Null
 $explorer = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$explorerCom.HWND)
 Wait-Until { @($explorer.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and $_.Current.Name -eq '項目ビュー' }).Count -gt 0 }
 $name = 'Clipboard-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt'
 Set-Content -LiteralPath (Join-Path $left $name) -Value 'Clipboard test'
 Wait-Until { $null -ne (Find-Item $window $name) }
 [ClipboardInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 $item = Find-Item $window $name
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
 Start-Sleep -Milliseconds 300
 [System.Windows.Forms.SendKeys]::SendWait('^c')
 if (![System.Windows.Forms.Clipboard]::GetFileDropList().Contains((Join-Path $left $name))) { throw '試作のCtrl+Cで検証ファイルがクリップボードに入りません。' }
 'PASS: 試作からのコピー内容をクリップボードで確認'
 [ClipboardInput]::SetForegroundWindow([IntPtr]$explorerCom.HWND) | Out-Null
 Wait-Until { @($explorer.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and $_.Current.Name -eq '項目ビュー' }).Count -gt 0 }
 $list = $explorer.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and $_.Current.Name -eq '項目ビュー' } | Select-Object -First 1
 $rect = $list.Current.BoundingRectangle
 [ClipboardInput]::SetCursorPos([int]($rect.X+$rect.Width/2),[int]($rect.Bottom-65)) | Out-Null
 [ClipboardInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 [ClipboardInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 [System.Windows.Forms.SendKeys]::SendWait('^v')
 Wait-Until { Test-Path -LiteralPath (Join-Path $external $name) }
 'PASS: 試作でCtrl+C、ExplorerでCtrl+V'
 Wait-Until { $null -ne (Find-Item $explorer $name) }
 $item = Find-Item $explorer $name
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
 [System.Windows.Forms.SendKeys]::SendWait('^x')
 [ClipboardInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 $lists = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List })
 $rect = $lists[1].Current.BoundingRectangle
 [ClipboardInput]::SetCursorPos([int]($rect.X+$rect.Width/2),[int]($rect.Bottom-65)) | Out-Null
 [ClipboardInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 [ClipboardInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 [System.Windows.Forms.SendKeys]::SendWait('^v')
 Wait-Until { (Test-Path -LiteralPath (Join-Path $right $name)) -and !(Test-Path -LiteralPath (Join-Path $external $name)) }
 'PASS: ExplorerでCtrl+X、試作でCtrl+V'
} finally { $explorerCom.Quit() }
