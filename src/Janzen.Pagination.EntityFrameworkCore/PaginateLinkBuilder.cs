using System.Globalization;

namespace Janzen.Pagination.EntityFrameworkCore;

internal static class PaginateLinkBuilder {

	public static PaginatedLinks Build(PaginateLinkContext? context, int currentPage, int totalPages) {
		if (context is null) return new PaginatedLinks(string.Empty, string.Empty, string.Empty, string.Empty);

		int lastPage = Math.Max(totalPages, 1);

		return new PaginatedLinks(
			BuildLink(context, 1),
			currentPage > 1 ? BuildLink(context, currentPage - 1) : string.Empty,
			totalPages > 0 && currentPage < totalPages ? BuildLink(context, currentPage + 1) : string.Empty,
			BuildLink(context, lastPage)
		);
	}

	private static string BuildLink(PaginateLinkContext context, int page) {
		var values = new List<KeyValuePair<string, string>>(context.QueryParameters.Count + 1);

		foreach (var pair in context.QueryParameters) {
			if (string.Equals(pair.Key, "page", StringComparison.OrdinalIgnoreCase)) continue;

			values.Add(pair);
		}

		values.Add(new KeyValuePair<string, string>("page", page.ToString(CultureInfo.InvariantCulture)));

		string query = string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

		return query.Length == 0 ? context.Path : $"{context.Path}?{query}";
	}

}
