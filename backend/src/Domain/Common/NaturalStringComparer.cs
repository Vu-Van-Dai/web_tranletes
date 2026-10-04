namespace ComicPlatform.Domain.Common;

/// <summary>
/// Orders "page2.jpg" before "page10.jpg": digit runs are compared numerically,
/// everything else ordinal-ignore-case.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var a = x.AsSpan(startI, i - startI).TrimStart('0');
                var b = y.AsSpan(startJ, j - startJ).TrimStart('0');
                if (a.Length != b.Length)
                {
                    return a.Length.CompareTo(b.Length);
                }

                var cmp = a.SequenceCompareTo(b);
                if (cmp != 0)
                {
                    return cmp;
                }

                // "01" vs "1": shorter raw run first for a stable total order.
                var rawCmp = (i - startI).CompareTo(j - startJ);
                if (rawCmp != 0)
                {
                    return rawCmp;
                }
            }
            else
            {
                var cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (cmp != 0)
                {
                    return cmp;
                }

                i++;
                j++;
            }
        }

        var lengthCmp = (x.Length - i).CompareTo(y.Length - j);
        return lengthCmp != 0 ? lengthCmp : string.CompareOrdinal(x, y);
    }
}
