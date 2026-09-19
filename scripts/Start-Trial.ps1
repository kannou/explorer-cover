$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$trial = Join-Path $root ('artifacts\try-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,4))
$left = Join-Path $trial '左ペイン'
$right = Join-Path $trial '右ペイン'
New-Item -ItemType Directory -Path $left,$right,(Join-Path $left '日本語フォルダー') | Out-Null
Set-Content -LiteralPath (Join-Path $left 'コピーしてみる.txt') -Value '左右間のコピーやドラッグ＆ドロップを試すためのファイルです。' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $left '名前を変えてみる.txt') -Value 'F2で名前を変更できます。' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $right '削除してみる.txt') -Value 'Deleteでごみ箱への移動を確認できます。' -Encoding UTF8
Write-Host "試用フォルダー: $trial"
& (Join-Path $root 'run.ps1') -LeftPath $left -RightPath $right
exit $LASTEXITCODE
