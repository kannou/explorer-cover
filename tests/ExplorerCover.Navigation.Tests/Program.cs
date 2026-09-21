using System.IO;
using System.Windows;
using System.Windows.Controls;
using ExplorerCover.Shell;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var result = 1;
        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "navigation-" + Guid.NewGuid().ToString("N"));
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        var pending = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Task<byte[]> Resolve(string path, CancellationToken token) => pending.TryGetValue(path, out var source) ? source.Task : ShellPathResolver.ResolveAsync(path, token);
        var host = new ExplorerHost(first, Resolve, TimeSpan.FromSeconds(2));
        var other = new ExplorerHost(first);
        var errors = new List<string>(); var completions = 0;
        host.Error += errors.Add; host.Navigated += _ => completions++;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        grid.Children.Add(host); Grid.SetColumn(other, 1); grid.Children.Add(other);
        var window = new Window { Title = "explorer-cover navigation verification", Width = 800, Height = 400, Content = grid };
        var app = new Application();
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Until(() => host.CurrentPath == first && other.CurrentPath == first && !host.IsNavigating);
                var bytes = await ShellPathResolver.ResolveAsync(first, CancellationToken.None);
                string Delay(string name) { var path = Path.Combine(root, name); pending.Add(path, new(TaskCreationOptions.RunContinuationsAsynchronously)); return path; }
                var stale = Delay("stale"); host.Navigate(stale);
                Assert(host.IsNavigating && host.CanNavigate, "確認中の再指定ができない");
                other.Navigate(second); await Until(() => other.CurrentPath == second);
                host.Navigate(second); await Until(() => host.CurrentPath == second && !host.IsNavigating);
                var count = completions; pending[stale].SetResult(bytes); await Task.Delay(150);
                Assert(host.CurrentPath == second && completions == count && errors.Count == 0, "古い結果が現在地を書き換えた");
                Console.WriteLine("PASS: 確認中も他ペインが応答し、新しい要求を優先して古い結果を破棄");
                var timeout = Delay("timeout"); host.Navigate(timeout);
                await Until(() => errors.Count == 1);
                Assert(!host.IsNavigating && host.CanNavigate && host.CurrentPath == second, "タイムアウトから回復できない");
                pending[timeout].SetResult(bytes); await Task.Delay(150);
                Assert(completions == count, "タイムアウト後の結果を採用した");
                host.Navigate(first); await Until(() => host.CurrentPath == first && !host.IsNavigating);
                Console.WriteLine("PASS: タイムアウトで現在地を保持し、再試行できる");
                var invalid = Delay("invalidated"); host.Navigate(invalid); host.Navigate("\0");
                await Until(() => errors.Count == 2); count = completions;
                pending[invalid].SetResult(bytes); await Task.Delay(150);
                Assert(completions == count && host.CurrentPath == first, "不正入力で取消した結果を採用した");
                Console.WriteLine("PASS: 不正な次要求でも前の確認結果を破棄");
                var closed = Delay("closed"); host.Navigate(closed); host.Dispose();
                pending[closed].SetResult(bytes); await Task.Delay(150);
                Assert(completions == count && errors.Count == 2, "破棄後に通知した");
                Console.WriteLine("PASS: タブ破棄後の結果とエラーを通知しない");
                result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); }
            finally { host.Dispose(); other.Dispose(); window.Close(); }
        };
        app.Run(window); return result;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Until(Func<bool> check)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!check()) { if (DateTime.UtcNow >= until) throw new TimeoutException(); await Task.Delay(20); }
    }
}
