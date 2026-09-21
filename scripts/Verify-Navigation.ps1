param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
if (!$window) { throw '試作のウィンドウが見つかりません。' }
function Get-Addresses {
    @($window.FindAll($scope, $all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and $_.Current.ClassName -eq 'TextBox' })
}
function Read-Address($element) { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Wait-Until([scriptblock]$Check) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw '操作結果の待機がタイムアウトしました。'
}
$addresses = Get-Addresses
if ($addresses.Count -ne 2) { throw "パス欄が2つではありません: $($addresses.Count)" }
$left = Read-Address $addresses[0]
$right = Read-Address $addresses[1]
$buttons = @($window.FindAll($scope, $all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -eq '移動' })
$go = $buttons[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$value = $addresses[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$child = Join-Path $left '日本語フォルダー'
$value.SetValue($child); $go.Invoke()
Wait-Until { @($window.FindAll($scope, $all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -eq $child }).Count -eq 1 }
if ((Read-Address $addresses[1]) -ne $right) { throw '右ペインまで移動しました。' }
'PASS: 日本語パスへの移動と左右の独立'
$value.SetValue((Join-Path $left '__does_not_exist__')); $go.Invoke()
Wait-Until { @($window.FindAll($scope, $all) | Where-Object { $_.Current.Name -like '移動できません:*' }).Count -gt 0 }
'PASS: 存在しないパスのエラー表示と継続動作'
$value.SetValue($left); $go.Invoke()
Wait-Until { @($window.FindAll($scope, $all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語フォルダー' }).Count -eq 1 }
$folder = $window.FindAll($scope, $all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語フォルダー' } | Select-Object -First 1
$folder.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Wait-Until { (Read-Address $addresses[0]) -eq $child }
'PASS: シェル一覧からのフォルダー移動とパス欄の追従'
$value.SetValue($left); $go.Invoke()
Wait-Until { @($window.FindAll($scope, $all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語フォルダー' }).Count -eq 1 }
$transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
$transform.Resize(1000, 600)
Wait-Until { [Math]::Abs($window.Current.BoundingRectangle.Width - 1000) -lt 3 }
$transform.Resize(1200, 740)
'PASS: ウィンドウのリサイズ'
