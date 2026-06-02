using System.Diagnostics.CodeAnalysis;

namespace Janzen.Pagination.EntityFrameworkCore;

// Replaces the app-internal StringExtensions dependency for the pagination packages.
internal static class PaginateStringExtensions
{
    extension([NotNullWhen(true)] string? value)
    {
        public bool IsFilled() => !string.IsNullOrWhiteSpace(value);
    }

    extension([NotNullWhen(false)] string? value)
    {
        public bool IsNullOrWhiteSpace() => string.IsNullOrWhiteSpace(value);
    }
}
