# 第三者ライセンス・同梱物

## explorer-cover

このリポジトリのexplorer-coverのコードは、ルートの[`LICENSE`](LICENSE)に記載したMIT Licenseで提供します。

## .NET / WPF

`publish.cmd`で作成する自己完結型のWindows x64配布物には、Microsoft .NETのランタイムとWPFのアセンブリが含まれます。配布物に含まれるランタイムについては、Microsoft .NET Library Licenseと、ランタイムが参照する第三者コンポーネントの通知が適用されます。配布物の`licenses\dotnet`に、使用しているSDKに含まれる次の文書を同梱します。

- [`LICENSE.txt`](licenses/dotnet/LICENSE.txt)
- [`ThirdPartyNotices.txt`](licenses/dotnet/ThirdPartyNotices.txt)

ライセンスの原文と配布条件は、[.NETのライセンス情報](https://github.com/dotnet/core/blob/main/license-information.md)と[.NET Runtimeの配布時ライセンス案内](https://github.com/dotnet/runtime/blob/main/docs/project/licensing-assets.md)を参照してください。

## QuickLook

QuickLookの実行ファイルやソースコードは、このリポジトリにも配布物にも含めていません。explorer-coverは、ユーザーが別途インストールしたQuickLookへ実行時に接続します。QuickLookを利用する場合は、[QL-Win/QuickLook](https://github.com/QL-Win/QuickLook)から入手し、同プロジェクトのGPL-3.0および同梱文書に従ってください。

## Windows Shell / Win32

ファイル一覧とシェル操作には、Windowsに標準搭載されているWindows ShellおよびWin32 APIを使用しています。これらのAPIの実装やWindows本体はこのプロジェクトから再配布していません。
