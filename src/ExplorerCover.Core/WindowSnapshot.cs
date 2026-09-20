namespace ExplorerCover.Core;

// 通常表示時の画面上の矩形（物理ピクセル）。最小化は保存しない。
public sealed record WindowSnapshot(int Left, int Top, int Width, int Height, bool Maximized)
{
    public bool IsValid => Left is > -1000000 and < 1000000 && Top is > -1000000 and < 1000000 && Width is > 0 and < 100000 && Height is > 0 and < 100000;
    public WindowSnapshot FitToWorkArea(int left, int top, int width, int height)
    {
        if (!IsValid || width <= 0 || height <= 0) throw new ArgumentException("ウィンドウまたは作業領域の矩形が不正です。");
        var fittedWidth = Math.Min(Width, width); var fittedHeight = Math.Min(Height, height);
        return this with { Left = Math.Clamp(Left, left, left + width - fittedWidth), Top = Math.Clamp(Top, top, top + height - fittedHeight), Width = fittedWidth, Height = fittedHeight };
    }
}
