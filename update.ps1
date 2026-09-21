$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$dist = Join-Path $root 'dist'
$executable = Join-Path $dist 'explorer-cover\explorer-cover.exe'
$updateLock = $null

try {
    foreach ($path in @($dist, (Split-Path -Parent $executable), $executable)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "リンクを更新先には使えません: $path"
        }
    }
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    # 終了から再起動までの二重実行を防ぐ。発行自体のロックはpublish.ps1が保持する。
    $updateLock = [IO.File]::Open((Join-Path $dist 'update.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $running = @(Get-Process -Name explorer-cover -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.Equals($executable, [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($appProcess in $running) {
        try {
            if ($appProcess.HasExited) { continue }
            Write-Host "explorer-coverの終了を待っています (PID: $($appProcess.Id))..."
            # 通常の終了処理で作業状態を保存する。強制終了はしない。
            if (!$appProcess.CloseMainWindow() -and !$appProcess.HasExited) {
                throw '終了を要求できませんでした。設定画面などを閉じてから、update.cmdを再実行してください。'
            }
            if (!$appProcess.WaitForExit(30000)) {
                throw '30秒以内に終了しませんでした。explorer-coverのダイアログや処理状況を確認して、update.cmdを再実行してください。'
            }
        } finally { $appProcess.Dispose() }
    }

    Write-Host '通常版をビルド・発行しています...'
    # publish.ps1のexitと変数を分離し、既存の発行・退避処理をそのまま利用する。
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'publish.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw '発行に失敗したため、起動を中止しました。エラーを確認して再実行してください。'
    }
    if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw "起動ファイルが見つかりません: $executable" }
    # 日常利用するアプリのウィンドウを表示する。
    Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) | Out-Null
    Write-Host '更新したexplorer-coverを起動しました。'
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
} finally {
    if ($updateLock) { $updateLock.Dispose() }
}
