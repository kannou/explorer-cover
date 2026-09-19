param([string[]]$Profiles = @('default','custom','invalid'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class FoundationInput {
 public static uint Target;
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct Input {
  [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags;
 }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
 public static void Chord(params ushort[] keys) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if (current != Target) throw new InvalidOperationException("検証対象が前面にありません。");
  var inputs = new Input[keys.Length*2];
  for (int i=0;i<keys.Length;i++) { inputs[i]=new Input{Type=1,Key=keys[i]}; inputs[keys.Length+i]=new Input{Type=1,Key=keys[keys.Length-i-1],Flags=2}; }
  if (SendInput((uint)inputs.Length,inputs,40)!=inputs.Length) throw new InvalidOperationException("入力失敗: "+Marshal.GetLastWin32Error());
 }
}
'@
$root = Split-Path -Parent $PSScriptRoot
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$trial = Join-Path $root 'artifacts\trial'
New-Item -ItemType Directory -Force (Join-Path $trial 'left\日本語フォルダー'),(Join-Path $trial 'right') | Out-Null
$settings = Join-Path $root 'artifacts\foundation-shortcuts.json'
$previousConfig = $env:EXPLORER_COVER_SHORTCUTS
$previousLog = $env:EXPLORER_COVER_LOG
function Wait-Until([scriptblock]$check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(12)
 do { if (& $check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw '操作結果の待機がタイムアウトしました。'
}
function Elements { @($script:window.FindAll($scope,$all)) }
function Value($element) { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) }
function Press([ushort[]]$keys) { [FoundationInput]::Chord($keys); Start-Sleep -Milliseconds 250 }
try {
 foreach ($profile in $Profiles) {
  $json = switch ($profile) {
   'default' { '{"version":1,"bindings":{}}' }
   'custom' { '{"version":1,"bindings":{"focusAddress":["Ctrl+K"],"switchPane":["F8"],"navigateAddress":["Ctrl+G"]}}' }
   'invalid' { '{"version":1,"bindings":{"focusAddress":["F6"]}}' }
  }
  Set-Content -LiteralPath $settings -Value $json -Encoding utf8
  $env:EXPLORER_COVER_SHORTCUTS = $settings
  $env:EXPLORER_COVER_LOG = Join-Path $root "artifacts\foundation-$profile.log"
  $dll = Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer_cover.dll'
  $app = Start-Process -FilePath (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+(Join-Path $trial 'left')+'"'),('"'+(Join-Path $trial 'right')+'"')) -WindowStyle Hidden -PassThru
  $script:window = $null
  try {
   Wait-Until {
    $script:window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer_cover*' } | Select-Object -First 1
    $null -ne $script:window -and @((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List }).Count -eq 2
   }
   [FoundationInput]::Target = [uint32]$app.Id
   Wait-Until { @((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -in @((Join-Path $trial 'left'),(Join-Path $trial 'right')) }).Count -eq 2 }
   [FoundationInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
   if ($profile -eq 'default') {
    & (Join-Path $PSScriptRoot 'Verify-Keys.ps1') -ProcessId $app.Id
    & (Join-Path $PSScriptRoot 'Verify-Navigation.ps1') -ProcessId $app.Id
    'PASS: 初期設定で既存のキー操作・移動が成立'
   } elseif ($profile -eq 'custom') {
    $addresses = @((Elements) | Where-Object { $_.Current.ClassName -eq 'TextBox' })
    $addresses[0].SetFocus()
    Press @(0x1B)
    Press @(0x11,0x4C)
    if ($addresses[0].Current.HasKeyboardFocus) { throw '変更前のCtrl+Lがアプリの操作として残っています。' }
    Press @(0x11,0x4B)
    if (!$addresses[0].Current.HasKeyboardFocus) { throw 'Ctrl+Kでパス欄へ移りません。' }
    Press @(0x77)
    Press @(0x11,0x4B)
    if (!$addresses[1].Current.HasKeyboardFocus) { throw 'F8で右へ切り替わりません。' }
    $child = Join-Path $trial 'left\日本語フォルダー'
    (Value $addresses[1]).SetValue($child)
    Press @(0x0D)
    if (!$addresses[1].Current.HasKeyboardFocus) { throw '解除したEnterで移動しました。' }
    Press @(0x11,0x47)
    Wait-Until { @((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -eq $child }).Count -eq 1 }
    if ((Value $addresses[0]).Current.Value -ne (Join-Path $trial 'left')) { throw '別ペインまで移動しました。' }
    Press @(0x11,0x4B)
    (Value $addresses[1]).SetValue((Join-Path $trial 'right'))
    $buttons = @((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -eq '移動' })
    $buttons[1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-Until { @((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -eq (Join-Path $trial 'right') }).Count -eq 1 }
    if (!((Elements) | Where-Object { $_.Current.Name -like 'Ctrl+K: パス入力*F8: 左右切替*' })) { throw 'キー案内が設定と一致しません。' }
    'PASS: 変更キー・旧キー解除・左右対象・ボタンとキーの共通移動・キー案内'
   } else {
    if (!((Elements) | Where-Object { $_.Current.Name -like 'キー設定を読み込めないため初期値を使用します:*' })) { throw '不正設定の通知がありません。' }
    & (Join-Path $PSScriptRoot 'Verify-Keys.ps1') -ProcessId $app.Id
    'PASS: 競合するJSONから初期キーへ安全にフォールバック'
   }
  } finally {
   if ($window) { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
   if (!$app.WaitForExit(10000)) { throw '終了待機がタイムアウトしました。' }
   if ($app.ExitCode -ne 0) { throw "異常終了: $($app.ExitCode)" }
  }
 }
} finally { $env:EXPLORER_COVER_SHORTCUTS = $previousConfig; $env:EXPLORER_COVER_LOG = $previousLog }
