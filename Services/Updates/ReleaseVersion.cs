namespace CryptoJournal.Wpf.Services.Updates;

internal readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public int CompareTo(ReleaseVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0) return majorComparison;

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0 ? minorComparison : Patch.CompareTo(other.Patch);
    }

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];

        var parts = normalized.Split('.');
        if (parts.Length < 2 || parts.Length > 4) return false;

        if (!int.TryParse(parts[0], out var major) || major < 0 ||
            !int.TryParse(parts[1], out var minor) || minor < 0)
        {
            return false;
        }

        var patch = 0;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
            return false;

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
