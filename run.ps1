param([string]$LeftPath = $env:USERPROFILE, [string]$RightPath = $env:USERPROFILE)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path -Parent $sdk
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $sdk run --project (Join-Path $PSScriptRoot 'src\ExplorerAlt\ExplorerAlt.csproj') --configuration Release -- $LeftPath $RightPath
exit $LASTEXITCODE
