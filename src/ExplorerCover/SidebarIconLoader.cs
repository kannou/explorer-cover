using System.Windows.Controls;
using System.Windows.Media;

namespace ExplorerCover;

internal static class SidebarIconLoader
{
    // 行の削除時は共有のShell取得を止めず、この行の待機と参照だけを解放する。
    public static async Task LoadAsync(Image image, string path, Func<string?, Task<ImageSource?>> getIcon, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (image.Source == null)
            {
                var fallback = await getIcon(null).WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                image.Source = fallback;
            }
            var actual = await getIcon(path).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (actual != null) image.Source = actual;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
