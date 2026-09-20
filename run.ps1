param([string]$LeftPath, [string]$RightPath)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path -Parent $sdk
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$paths = @()
if ($PSBoundParameters.ContainsKey("LeftPath") -or $PSBoundParameters.ContainsKey("RightPath")) {
    $paths = @($(if ($LeftPath) { $LeftPath } else { $env:USERPROFILE }), $(if ($RightPath) { $RightPath } else { $env:USERPROFILE }))
}
& $sdk run --project (Join-Path $PSScriptRoot 'src\ExplorerCover\ExplorerCover.csproj') --configuration Release -- @paths
exit $LASTEXITCODE
