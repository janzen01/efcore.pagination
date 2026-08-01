using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Globalization;

namespace Janzen.Pagination.EntityFrameworkCore.Links;

internal static class PaginateLinkBuilder {

	public static PaginatedLinks? Build(PaginateLinkContext? context, int currentPage, int totalPages) {

		// No context, no links: an envelope with four null strings tells the caller nothing a null does not.
		if (context is null) return null;

		int lastPage = Math.Max(totalPages, 1);

		// Build the escaped non-page query prefix once and reuse it across all four links.
		string prefix = string.Join("&", context.QueryParameters
			.Where(pair => !string.Equals(pair.Key, PaginateQueryParams.Page, StringComparison.OrdinalIgnoreCase))
			.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

		return new PaginatedLinks(
			BuildLink(context.Path, prefix, 1),
			currentPage > 1 ? BuildLink(context.Path, prefix, currentPage - 1) : null,
			totalPages > 0 && currentPage < totalPages ? BuildLink(context.Path, prefix, currentPage + 1) : null,
			BuildLink(context.Path, prefix, lastPage)
		);

	}

	private static string BuildLink(string path, string prefix, int page) {

		string pageParam = $"{PaginateQueryParams.Page}={page.ToString(CultureInfo.InvariantCulture)}";
		string query = prefix.Length == 0 ? pageParam : $"{prefix}&{pageParam}";

		return $"{path}?{query}";

	}

}
