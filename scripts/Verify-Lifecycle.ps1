$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
$root = Split-Path -Parent $PSScriptRoot
$all = [System.Windows.Automation.Condition]::TrueCondition
$dll = Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer_cover.dll'
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
for ($cycle=1; $cycle -le 3; $cycle++) {
 $log = Join-Path $root "artifacts\lifecycle-$cycle.log"
 $env:EXPLORER_COVER_LOG = $log
 Set-Content -LiteralPath $log -Value ''
 $app = Start-Process -FilePath $sdk -ArgumentList @(('"'+$dll+'"'),('"'+(Join-Path $root 'artifacts\trial\left')+'"'),('"'+(Join-Path $root 'artifacts\trial\right')+'"')) -WindowStyle Hidden -PassThru
 $deadline = [DateTime]::UtcNow.AddSeconds(15)
 do {
  if ($app.HasExited) { throw "起動中に終了: $($app.ExitCode)" }
  $window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer_cover*' } | Select-Object -First 1
  $navigations = @(Select-String -LiteralPath $log -Pattern 'Navigation complete').Count
  if ($window -and $navigations -eq 2) { break }
  Start-Sleep -Milliseconds 100
 } while ([DateTime]::UtcNow -lt $deadline)
 if (!$window -or $navigations -ne 2) { throw '起動・両ペインの移動待機がタイムアウトしました。' }
 $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 if (!$app.WaitForExit(10000)) { throw '終了待機がタイムアウトしました。' }
 if ($app.ExitCode -ne 0) { throw "終了コード: $($app.ExitCode)" }
 $logText = Get-Content -LiteralPath $log -Raw
 if ($logText -match 'failed:|初期化できません' -or ([regex]::Matches($logText,'ExplorerBrowser released')).Count -ne 2) { throw 'シェル解放処理の確認に失敗しました。' }
 "PASS: 起動・左右の移動・両シェル解放・終了コード0 ($cycle/3)"
}
Remove-Item Env:\EXPLORER_COVER_LOG
