using System.Text.Json.Serialization;

namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     The pagination envelope: one page of <see cref="Items" /> plus paging <see cref="Meta" /> and
///     hypermedia <see cref="Links" />.
/// </summary>
public sealed record PaginatedResponse<T>(
	IReadOnlyList<T> Items,
	PaginatedMeta Meta,
	PaginatedLinks Links
);

/// <summary>
///     Paging metadata: <see cref="TotalItems" /> across all pages, <see cref="ItemCount" /> on this page,
///     <see cref="ItemsPerPage" />, <see cref="TotalPages" /> and the 1-based <see cref="CurrentPage" />.
/// </summary>
public sealed record PaginatedMeta(
	int TotalItems,
	int ItemCount,
	int ItemsPerPage,
	int TotalPages,
	int CurrentPage
);

/// <summary>
///     Hypermedia links for the page. Absent links (e.g. <see cref="Previous" /> on the first page) are
///     <see langword="null" /> and omitted from JSON, rather than emitted as empty strings.
/// </summary>
public sealed record PaginatedLinks(
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? First,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Previous,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Last
);
