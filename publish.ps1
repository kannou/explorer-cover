$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$dist = Join-Path $root 'dist'
$target = Join-Path $dist 'explorer-cover'
$id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$staging = Join-Path $dist ('.build-' + $id)
$backup = Join-Path $dist ('previous-' + $id)
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Assert-Closed {
    foreach ($process in @(Get-Process -Name explorer-cover -ErrorAction SilentlyContinue)) {
        if ($process.Path -and $process.Path.Equals((Join-Path $target 'explorer-cover.exe'), [StringComparison]::OrdinalIgnoreCase)) {
            throw '通常版のexplorer-coverを閉じてから、もう一度publish.cmdを実行してください。'
        }
    }
}
try {
    # 移動対象は固定のdist直下に限定し、リンク先への書き込みを防ぐ。
    foreach ($path in @($dist,$target,$staging,$backup)) {
        $resolved = [IO.Path]::GetFullPath($path)
        if ($resolved -ne $dist -and [IO.Path]::GetDirectoryName($resolved) -ne $dist) { throw '出力先がdist直下ではありません。' }
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "リンクを出力先には使えません: $path" }
    }
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $lock = [IO.File]::Open((Join-Path $dist 'publish.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    Assert-Closed
    & $sdk publish (Join-Path $root 'src\ExplorerCover\explorer-cover.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false --source https://api.nuget.org/v3/index.json -o $staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "発行に失敗しました。現在の通常版は変更していません。出力: $staging" }
    foreach ($file in @('explorer-cover.exe','explorer-cover.dll','coreclr.dll','hostfxr.dll','PresentationFramework.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $staging $file))) { throw "発行ファイルが不足しています: $file" }
    }
    Copy-Item -LiteralPath (Join-Path $root 'docs\RUNNING.md') -Destination (Join-Path $staging '使い方.md')
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $staging 'LICENSE-explorer-cover.txt')
    Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $staging 'THIRD-PARTY-NOTICES.md')
    $dotnetLicenseDir = Join-Path $staging 'licenses\dotnet'
    New-Item -ItemType Directory -Force -Path $dotnetLicenseDir | Out-Null
    $dotnetRoot = Split-Path -Parent $sdk
    $dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
    $dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
    if (!(Test-Path -LiteralPath $dotnetLicense)) { $dotnetLicense = Join-Path $root 'licenses\dotnet\LICENSE.txt' }
    if (!(Test-Path -LiteralPath $dotnetNotices)) { $dotnetNotices = Join-Path $root 'licenses\dotnet\ThirdPartyNotices.txt' }
    Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $dotnetLicenseDir 'LICENSE.txt')
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $dotnetLicenseDir 'ThirdPartyNotices.txt')
    Assert-Closed
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup }
    try { Move-Item -LiteralPath $staging -Destination $target }
    catch {
        if ((Test-Path -LiteralPath $backup) -and !(Test-Path -LiteralPath $target)) { Move-Item -LiteralPath $backup -Destination $target }
        throw
    }
    Write-Host "発行完了: $(Join-Path $target 'explorer-cover.exe')"
    if (Test-Path -LiteralPath $backup) { Write-Host "前の版: $backup" }
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
} finally { if ($lock) { $lock.Dispose() } }
