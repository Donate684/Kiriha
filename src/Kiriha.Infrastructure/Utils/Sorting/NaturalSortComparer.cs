namespace Kiriha.Infrastructure.Utils.Sorting;

public sealed class NaturalSortComparer : IComparer<string?>
{
    public static NaturalSortComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0;
        int iy = 0;
        int lenX = x.Length;
        int lenY = y.Length;

        while (ix < lenX && iy < lenY)
        {
            char cx = x[ix];
            char cy = y[iy];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int startX = ix;
                while (ix < lenX && char.IsDigit(x[ix])) ix++;

                int startY = iy;
                while (iy < lenY && char.IsDigit(y[iy])) iy++;

                ReadOnlySpan<char> numSpanX = x.AsSpan(startX, ix - startX);
                ReadOnlySpan<char> numSpanY = y.AsSpan(startY, iy - startY);

                int nonZeroX = 0;
                while (nonZeroX < numSpanX.Length && numSpanX[nonZeroX] == '0') nonZeroX++;

                int nonZeroY = 0;
                while (nonZeroY < numSpanY.Length && numSpanY[nonZeroY] == '0') nonZeroY++;

                ReadOnlySpan<char> sigX = numSpanX[nonZeroX..];
                ReadOnlySpan<char> sigY = numSpanY[nonZeroY..];

                if (sigX.Length != sigY.Length)
                {
                    return sigX.Length.CompareTo(sigY.Length);
                }

                int cmp = sigX.CompareTo(sigY, StringComparison.Ordinal);
                if (cmp != 0)
                {
                    return cmp;
                }

                if (numSpanX.Length != numSpanY.Length)
                {
                    return numSpanX.Length.CompareTo(numSpanY.Length);
                }
            }
            else
            {
                char upperX = char.ToUpperInvariant(cx);
                char upperY = char.ToUpperInvariant(cy);

                if (upperX != upperY)
                {
                    return upperX.CompareTo(upperY);
                }

                ix++;
                iy++;
            }
        }

        return lenX.CompareTo(lenY);
    }
}
