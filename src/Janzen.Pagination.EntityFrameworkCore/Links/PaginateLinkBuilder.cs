using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Globalization;

namespace Janzen.Pagination.EntityFrameworkCore.Links;

internal static class PaginateLinkBuilder {

	/// <summary>
	///     Builds the five links. <paramref name="navigablePages" /> is what <c>first</c> / <c>last</c> / <c>next</c>
	///     are drawn from — normally equal to <paramref name="totalPages" />, but smaller where the configuration
	///     caps the offset, so a link never points at a page the same config would answer with a 400.
	/// </summary>
	public static PaginatedLinks? Build(PaginateLinkContext? context, int currentPage, int totalPages, int navigablePages) {

		// No context, no links: an envelope with four null strings tells the caller nothing a null does not.
		if (context is null) return null;

		int lastPage = Math.Max(navigablePages, 1);

		// Build the escaped non-page query prefix once and reuse it across all four links.
		string prefix = string.Join("&", context.QueryParameters
			.Where(pair => !string.Equals(pair.Key, PaginateQueryParams.Page, StringComparison.OrdinalIgnoreCase))
			.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

		return new PaginatedLinks(
			BuildLink(context.Path, prefix, 1),
			currentPage > 1 ? BuildLink(context.Path, prefix, currentPage - 1) : null,
			totalPages > 0 && currentPage < navigablePages ? BuildLink(context.Path, prefix, currentPage + 1) : null,
			BuildLink(context.Path, prefix, lastPage)
		) {
			// The requested page, not a clamped one: this echoes the request, so it stays truthful past the last page.
			Current = BuildLink(context.Path, prefix, currentPage)
		};

	}

	private static string BuildLink(string path, string prefix, int page) {

		string pageParam = $"{PaginateQueryParams.Page}={page.ToString(CultureInfo.InvariantCulture)}";
		string query = prefix.Length == 0 ? pageParam : $"{prefix}&{pageParam}";

		return $"{path}?{query}";

	}

}
