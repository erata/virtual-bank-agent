namespace VirtualBank.Application.Authorization.Principals;

/// <summary>
/// A normalized, segment-aware organizational group path such as <c>/Credit/Analysis</c>.
/// Comparison is per segment, so <c>/Credit</c> never matches <c>/Creditors</c>.
/// </summary>
public sealed class GroupPath : IEquatable<GroupPath>
{
    private readonly string[] _segments;

    private GroupPath(string[] segments)
    {
        _segments = segments;
        Value = "/" + string.Join('/', segments);
    }

    public string Value { get; }

    public IReadOnlyList<string> Segments => _segments;

    public static bool TryParse(string? raw, out GroupPath? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(raw) || raw[0] != '/')
        {
            return false;
        }

        var segments = raw.Split('/');
        // Leading '/' produces one empty first element; any other empty segment ("//", trailing '/') is invalid.
        if (segments.Skip(1).Any(string.IsNullOrWhiteSpace) || segments.Length < 2)
        {
            return false;
        }

        path = new GroupPath(segments[1..]);
        return true;
    }

    public static GroupPath Parse(string raw) =>
        TryParse(raw, out var path) ? path! : throw new FormatException($"Invalid group path '{raw}'.");

    /// <summary>The path itself followed by all ancestors, e.g. /Credit/Analysis then /Credit.</summary>
    public IEnumerable<GroupPath> SelfAndAncestors()
    {
        for (var length = _segments.Length; length >= 1; length--)
        {
            yield return new GroupPath(_segments[..length]);
        }
    }

    /// <summary>True when this path equals <paramref name="other"/> or lies below it.</summary>
    public bool IsSameOrDescendantOf(GroupPath other)
    {
        if (other.Segments.Count > Segments.Count)
        {
            return false;
        }

        for (var i = 0; i < other.Segments.Count; i++)
        {
            if (!string.Equals(Segments[i], other.Segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(GroupPath? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GroupPath other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}
