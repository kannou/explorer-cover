param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TestInput {
 public static uint TargetProcessId;
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
 [StructLayout(LayoutKind.Explicit, Size=40)] public struct Input {
  [FieldOffset(0)] public uint Type;
  [FieldOffset(8)] public ushort Key;
  [FieldOffset(12)] public uint Flags;
 }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
 public static void Chord(params ushort[] keys) {
  uint current;
  GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if (current != TargetProcessId) throw new InvalidOperationException("検証対象が前面にないため入力を中止しました。");
  var inputs = new Input[keys.Length*2];
  for (int i=0;i<keys.Length;i++) {
   inputs[i] = new Input { Type=1, Key=keys[i] };
   inputs[keys.Length+i] = new Input { Type=1, Key=keys[keys.Length-i-1], Flags=2 };
  }
  uint sent = SendInput((uint)inputs.Length,inputs,40);
  if (sent != inputs.Length) throw new InvalidOperationException("SendInput: " + sent + "/" + inputs.Length + ", error=" + Marshal.GetLastWin32Error());
 }
}
'@
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'explorer_cover*' } | Select-Object -First 1
if (!$window) { throw '試作のウィンドウが見つかりません。' }
[TestInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
[TestInput]::TargetProcessId = [uint32]$ProcessId
$addresses = @($window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and $_.Current.ClassName -eq 'TextBox' })
$left = $addresses[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$right = $addresses[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$testRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\trial'))
if ($left -ne (Join-Path $testRoot 'left') -or $right -ne (Join-Path $testRoot 'right')) { throw '検証専用フォルダー以外では実行できません。' }
$name = 'キー操作-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt'
Set-Content -LiteralPath (Join-Path $left $name) -Value 'キー操作の検証'
$deadline = [DateTime]::UtcNow.AddSeconds(10)
do {
    $file = $window.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -in @($name,[IO.Path]::GetFileNameWithoutExtension($name)) } | Select-Object -First 1
    if ($file) { break }
    Start-Sleep -Milliseconds 100
} while ([DateTime]::UtcNow -lt $deadline)
if (!$file) { throw '検証用ファイルが見つかりません。' }
$file.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$file.SetFocus()
Start-Sleep -Milliseconds 250
[TestInput]::Chord(0x11,0x43)
Start-Sleep -Milliseconds 300
[TestInput]::Chord(0x75)
Start-Sleep -Milliseconds 300
[TestInput]::Chord(0x11,0x56)
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while (!(Test-Path -LiteralPath (Join-Path $right $name)) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
if (!(Test-Path -LiteralPath (Join-Path $right $name))) { throw 'Ctrl+C / F6 / Ctrl+Vの結果が確認できません。' }
'PASS: Ctrl+C、F6による右ペイン切替、Ctrl+Vによる実ファイルのコピー'
[TestInput]::Chord(0x11,0x4C)
Start-Sleep -Milliseconds 300
if (!$addresses[1].Current.HasKeyboardFocus) { throw 'Ctrl+Lで右パス欄にフォーカスが移りません。' }
'PASS: Ctrl+Lによるパス欄へのフォーカス移動'
[TestInput]::Chord(0x1B)
Start-Sleep -Milliseconds 300
if ($addresses[1].Current.HasKeyboardFocus) { throw 'Escでパス欄から移動しません。' }
'PASS: Escによる一覧への復帰'
