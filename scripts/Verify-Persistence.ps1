
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TabInput {
 public static uint Target;
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct Input {
  [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags;
  [FieldOffset(20)] public uint MouseFlags;
 }
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 public static void Hover(int x,int y) { SetCursorPos(x,y); System.Threading.Thread.Sleep(250); }
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public static void Drag(int x,int y,int targetX,int targetY) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if(current!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  if(!SetCursorPos(x,y)) throw new InvalidOperationException("ポインタを移動できません。");
  System.Threading.Thread.Sleep(100);
  if(SendInput(1,new[]{new Input{MouseFlags=2}},40)!=1) throw new InvalidOperationException("入力送信失敗");
  try { System.Threading.Thread.Sleep(150); for(int i=1;i<=20;i++){SetCursorPos(x+(targetX-x)*i/20,y+(targetY-y)*i/20);System.Threading.Thread.Sleep(20);} }
  finally { SendInput(1,new[]{new Input{MouseFlags=4}},40); }
 }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
 public static void Chord(params ushort[] keys) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if(current!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  var inputs=new Input[keys.Length*2];
  // 左Ctrl+Tabを常駐ツールが置き換える環境でも、アプリの標準割り当てを検証する。
  if (Array.IndexOf(keys,(ushort)9)>=0)
   for(int i=0;i<keys.Length;i++) if(keys[i]==0x11) keys[i]=0xA3;
  for(int i=0;i<keys.Length;i++) {
   var down=keys[i]; var up=keys[keys.Length-i-1];
   inputs[i]=new Input{Type=1,Key=down,Flags=down==0xA3 ? 1u : 0u};
   inputs[keys.Length+i]=new Input{Type=1,Key=up,Flags=2u | (up==0xA3 ? 1u : 0u)};
  }
  if(SendInput((uint)inputs.Length,inputs,40)!=inputs.Length) throw new InvalidOperationException("入力送信に失敗しました。");
 }
}
"@
[TabInput]::SetProcessDPIAware() | Out-Null
$root = Split-Path -Parent $PSScriptRoot
$trial = Join-Path $root ('artifacts\persistence-' + [Guid]::NewGuid().ToString('N'))
$left = Join-Path $trial 'left'
$right = Join-Path $trial 'right'
$a = Join-Path $left '日本語 A'
$b = Join-Path $left 'B'
$c = Join-Path $left 'C'
New-Item -ItemType Directory -Force $a,$b,$c,$right | Out-Null
Set-Content (Join-Path $left 'selection.txt') 'tab selection test'
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
function Wait-Until([scriptblock]$check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(30)
 do { if (& $check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw ("操作結果の待機がタイムアウトしました。" + (Get-PSCallStack | Out-String) + ((Elements | Where-Object { $_.Current.AutomationId -like '*Status' } | ForEach-Object { $_.Current.AutomationId + '=' + $_.Current.Name }) -join ';'))
}
function Elements { if ($null -ne $script:window) { @($script:window.FindAll($scope,$all)) } }
function Find([string]$id) { $script:window.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
function Value([string]$side) { (Find ($side+'Address')).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) }
function Invoke([string]$id) { (Find $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 150 }
function Press([uint16[]]$keys) {
 if ($CustomKeys) {
  switch ($keys -join ',') {
   '17,9' { $keys = @(0x77) }
   '17,16,9' { $keys = @(0x76) }
   '18,37' { $keys = @(0x78) }
  }
 }
 [TabInput]::Chord($keys); Start-Sleep -Milliseconds 200
}
function At([string]$side,[string]$path) { Wait-Until { (Find ($side+'Status')).Current.Name -eq $path }; Start-Sleep -Milliseconds 150 }
function Go([string]$side,[string]$path) { (Value $side).SetValue($path); Invoke ($side+'.navigateAddress'); At $side $path }
function Tabs([string]$side) { @((Elements) | Where-Object { $_.Current.AutomationId -like ($side+'.tab.*') }) }
function Assert([bool]$value,[string]$message) { if (!$value) { throw $message } }
$oldConfig=$env:EXPLORER_COVER_SHORTCUTS; $oldLog=$env:EXPLORER_COVER_LOG
$settings=Join-Path $trial 'keys.json'; Set-Content $settings '{"version":1,"bindings":{}}' -Encoding utf8
$env:EXPLORER_COVER_SHORTCUTS=$settings; $log=Join-Path $trial 'app.log'; $env:EXPLORER_COVER_LOG=$log
$dll=Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer_cover.dll'
$oldState=$env:EXPLORER_COVER_STATE
$statePath=Join-Path $trial 'workspace.json'; $env:EXPLORER_COVER_STATE=$statePath
$missing='\\wsl.localhost\Ubuntu-24.04\home\explorer-cover-missing-'+[Guid]::NewGuid().ToString('N')
$seed=@{version=1;left=@{paths=@($a,$missing,$b);selectedIndex=2};right=@{paths=@($right);selectedIndex=0};activePane='right';leftPaneRatio=0.6;sidebarWidth=300;bookmarks=@()}
$seed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
function Start-App {
 $script:app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"')) -WindowStyle Hidden -PassThru
 $script:window=$null
 Wait-Until {
  if($app.HasExited){throw '起動に失敗'}
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object {$_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer_cover*'} | Select-Object -First 1
  $null -ne $script:window
 }
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
}
function Close-App {
 $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Assert ($app.WaitForExit(10000)) '終了時の保存が完了しない'
 $script:window=$null
}
function Saved { Get-Content -LiteralPath $statePath -Raw -Encoding utf8 | ConvertFrom-Json }
try {
 Start-App
 At '左' $b; At '右' $right
 Assert ((Tabs '左').Count -eq 3) '左の3タブが復元されない'
 Assert (@((Elements) | Where-Object {$_.Current.AutomationId -like 'Sidebar.Bookmark.*'}).Count -eq 0) '空のブックマークへ初期項目を追加した'
 Wait-Until {(Saved).activePane -eq 'right'}
 Start-Sleep -Seconds 1
 Assert (@(Select-String -LiteralPath $log -Pattern 'Creating ExplorerBrowser').Count -eq 2) '非選択のWSLタブを先に読み込んだ'
 'PASS: タブ順・選択・アクティブ右ペイン・空ブックマークを復元し、非選択タブは未読込'
 (Find '左Address').SetFocus(); Invoke 'Sidebar.AddBookmark'
 $sideRect=(Find 'Sidebar.Splitter').Current.BoundingRectangle
 [TabInput]::Drag([int]($sideRect.X+2),[int]($sideRect.Y+100),[int]($sideRect.X+42),[int]($sideRect.Y+100))
 $paneRect=(Find 'Pane.Splitter').Current.BoundingRectangle
 [TabInput]::Drag([int]($paneRect.X+3),[int]($paneRect.Y+100),[int]($paneRect.X-47),[int]($paneRect.Y+100))
 # 登録名の変更と順序変更は状態の保存テストでも確認済み。ここでは実画面の結果を保存する。
 $missingTab=(Tabs '左')[1]; $missingTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Wait-Until {(Find '左Status').Current.Name -like '移動できません:*'}
 (Value '右').SetValue($c); Invoke '右.navigateAddress'; At '右' $c
 (Find '左Address').SetFocus()
 Wait-Until {(Saved).left.selectedIndex -eq 1 -and (Saved).right.paths[0] -eq $c -and (Saved).bookmarks.Count -eq 1}
 $before=Saved
 Assert ($before.sidebarWidth -gt 320) 'サイドビュー幅を保存していない'
 Assert ([Math]::Abs($before.leftPaneRatio-0.6) -gt 0.02) 'ペイン比率を保存していない'
 (Value '左').SetValue('これは未確定の入力')
 Close-App
 $closed=Saved
 Assert ($closed.left.paths[1] -eq $missing) '到達不能な保存パスを失った'
 Assert ($closed.left.paths[1] -ne 'これは未確定の入力') '未確定入力を保存した'
 Assert ($closed.activePane -eq 'left') '終了直前の操作対象を保存していない'
 'PASS: 自動保存・終了時保存、左右幅とサイドビュー幅、登録、到達不能なWSLタブを保持'
 Start-App
 At '右' $c; Wait-Until {(Find '左Status').Current.Name -like '移動できません:*'}
 Assert ((Tabs '左').Count -eq 3) '再起動後にタブを失った'
 Assert ((Value '左').Current.Value -eq $missing) '失敗したWSLタブのパスを復元していない'
 Assert (@((Elements) | Where-Object {$_.Current.AutomationId -like 'Sidebar.Bookmark.*' -and $_.Current.Name -eq 'B'}).Count -eq 1) 'ブックマークを復元していない'
 $after=Saved
 Assert ([Math]::Abs($after.sidebarWidth-$before.sidebarWidth) -lt 0.1) 'サイドビュー幅が変わった'
 Assert ([Math]::Abs($after.leftPaneRatio-$before.leftPaneRatio) -lt 0.001) '左右幅比率が変わった'
 Go '左' $a; Invoke '左.parent'; At '左' $left
 'PASS: 再起動後の復元と、到達不能なタブから別パスへの再試行'
 Close-App
 # 壊れた本体から前回の正常保存へ復元し、元ファイルを退避する。
 Set-Content -LiteralPath $statePath 'broken json' -Encoding utf8
 Start-App; Wait-Until {$null -ne (Find '右Status') -and (Find '右Status').Current.Name -eq $c}
 Assert (@(Get-ChildItem -LiteralPath $trial -Filter '*.invalid-*').Count -eq 1) '破損ファイルを退避していない'
 Close-App
 'PASS: 破損ファイルでも起動してバックアップを復元'
 # 実際に置換を拒否し、保存エラー表示と「終了をやめて再試行」を確認する。
 Start-App; At '右' $c
 $lock=[IO.File]::Open($statePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try {
  Go '右' $right
  Wait-Until {@((Elements) | Where-Object {$_.Current.Name -like '作業状態を保存できません:*'}).Count -gt 0}
  $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  Wait-Until {@((Elements) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -like 'いいえ*'}).Count -gt 0}
  $no=(Elements) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -like 'いいえ*'} | Select-Object -First 1
  $no.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Assert (!$app.HasExited) '保存失敗後に終了を取り消せなかった'
 } finally { $lock.Dispose() }
 Close-App
 Assert ((Saved).right.paths[0] -eq $right) '保存失敗後に再試行できなかった'
 'PASS: 保存先の置換失敗を通知し、終了を取り消して再保存できる'
} finally {
 if($script:window){try{Close-App}catch{Write-Warning $_}}
 $env:EXPLORER_COVER_STATE=$oldState; $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog
 Write-Output ('検証記録: '+$trial)
}