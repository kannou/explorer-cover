$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist\explorer-cover\explorer-cover.exe'
$trial = Join-Path $root ('artifacts\published-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $trial | Out-Null
$names = @('EXPLORER_COVER_STATE','EXPLORER_COVER_SETTINGS','EXPLORER_COVER_LOG','DOTNET_ROOT','DOTNET_ROOT_X64')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$env:EXPLORER_COVER_STATE = Join-Path $trial 'workspace.json'
$env:EXPLORER_COVER_SETTINGS = Join-Path $trial 'input.json'
$env:EXPLORER_COVER_LOG = Join-Path $trial 'app.log'
$env:DOTNET_ROOT = Join-Path $trial 'no-runtime'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
function Wait-Until([scriptblock]$check) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do { if (& $check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw '通常版の起動・終了がタイムアウトしました。'
}
function Start-App {
    $script:app = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    Wait-Until {
        if ($app.HasExited) { throw "通常版の起動失敗: $($app.ExitCode)" }
        $script:window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
        $null -ne $script:window
    }
}
function Close-App {
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$app.WaitForExit(10000)) { throw '通常版を終了できません。' }
    $script:window = $null
}
try {
    Start-App
    'PASS: DOTNET_ROOTにランタイムがない状態でも通常版exeが起動'
    $before = (Get-FileHash -LiteralPath $exe).Hash
    $ErrorActionPreference = 'Continue' # この呼び出しは更新拒否による非ゼロ終了を期待する。
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'publish.ps1') *> (Join-Path $trial 'running-update.log')
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0 -or (Get-FileHash -LiteralPath $exe).Hash -ne $before) { throw '実行中の通常版を更新してしまいました。' }
    'PASS: 実行中の更新は拒否し、通常版exeを保持'
    Close-App
    if (!(Test-Path -LiteralPath $env:EXPLORER_COVER_STATE)) { throw '終了時に作業状態が保存されません。' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'publish.ps1') *> (Join-Path $trial 'update.log')
    if ($LASTEXITCODE -ne 0) { throw '終了後の更新に失敗しました。update.logを確認してください。' }
    $backup = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Directory -Filter 'previous-*' | Sort-Object Name -Descending | Select-Object -First 1
    if (!$backup -or (Get-FileHash -LiteralPath (Join-Path $backup.FullName 'explorer-cover.exe')).Hash -ne $before) { throw '前の版が退避されていません。' }
    Start-App; Close-App
    'PASS: 終了後の更新・前の版の退避・更新版の再起動と状態保存'
} finally {
    if ($script:window) { Close-App }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
    Write-Output ('検証記録: ' + $trial)
}
