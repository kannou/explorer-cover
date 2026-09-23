namespace ExplorerCover.Core;

// 表示用のパスは変更せず、履歴とブックマークで同じ比較規則を使う。
internal readonly struct LocationKey : IEquatable<LocationKey>
{
    private readonly string path;
    private readonly string? distro;

    private LocationKey(string path, string? distro = null) { this.path = path; this.distro = distro; }

    public static LocationKey Create(string path)
    {
        path = path.Replace('/', '\\').TrimEnd('\\');
        var unc = path;
        if (unc.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) unc = @"\\" + unc[8..];
        if (unc.StartsWith(@"\\"))
        {
            var parts = unc[2..].Split('\\', 3);
            if (parts.Length >= 2 && (parts[0].Equals("wsl.localhost", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("wsl$", StringComparison.OrdinalIgnoreCase)))
                return new(parts.Length == 3 ? parts[2] : "", parts[1]);
        }
        return new(path);
    }

    public bool Equals(LocationKey other) =>
        StringComparer.OrdinalIgnoreCase.Equals(distro, other.distro) &&
        (distro == null ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Equals(path, other.path);

    public override bool Equals(object? obj) => obj is LocationKey other && Equals(other);
    public override int GetHashCode() => distro == null
        ? StringComparer.OrdinalIgnoreCase.GetHashCode(path)
        : HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(distro), StringComparer.Ordinal.GetHashCode(path));
}
