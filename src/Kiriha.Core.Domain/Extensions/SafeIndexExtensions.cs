using System;
using System.Collections.Generic;

namespace Kiriha.Core.Domain.Extensions;

/// <summary>
/// C# 15 extension indexers providing safe out-of-bounds access for collections.
/// </summary>
public static class SafeIndexExtensions
{
    extension<T>(IReadOnlyList<T> list)
    {
        /// <summary>
        /// Safely retrieves the element at <paramref name="index"/>, or <c>default</c> if out of bounds.
        /// </summary>
        public T? this[int index, bool safe] =>
            (index >= 0 && index < list.Count) ? list[index] : default;
    }
}
