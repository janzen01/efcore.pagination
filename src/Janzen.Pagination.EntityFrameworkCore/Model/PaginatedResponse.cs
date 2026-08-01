namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     The pagination envelope: one page of <see cref="Items" /> plus paging <see cref="Meta" /> and, when a
///     link context was supplied, hypermedia <see cref="Links" />.
/// </summary>
/// <typeparam name="T">The item type of the page — the projection's result type, not the entity.</typeparam>
/// <param name="Items">The rows on this page, in the query's sort order. Empty past the last page.</param>
/// <param name="Meta">Paging counters for this page: see <see cref="PaginatedMeta" />.</param>
/// <param name="Links">Hypermedia links, or <see langword="null" /> as a whole when the call supplied no link context. Serialized as <c>null</c>, never omitted.</param>
public sealed record PaginatedResponse<T>(
	IReadOnlyList<T> Items,
	PaginatedMeta Meta,
	PaginatedLinks? Links
);

/// <summary>
///     Paging metadata: <see cref="TotalItems" /> across all pages, <see cref="ItemCount" /> on this page,
///     <see cref="ItemsPerPage" />, <see cref="TotalPages" /> and the 1-based <see cref="CurrentPage" />.
/// </summary>
/// <param name="TotalItems">Rows matching the filter and search across <b>all</b> pages, before paging.</param>
/// <param name="ItemCount">Rows actually returned on this page — smaller than <paramref name="ItemsPerPage" /> on the last page, and <c>0</c> past the end.</param>
/// <param name="ItemsPerPage">The effective page size for this request: the requested <c>limit</c>, or the configured default when it was omitted.</param>
/// <param name="TotalPages">Number of pages at this page size, or <c>0</c> when nothing matched.</param>
/// <param name="CurrentPage">The 1-based page that was <b>requested</b>. Not clamped, so it can exceed <paramref name="TotalPages" /> — that page is simply empty.</param>
public sealed record PaginatedMeta(
	int TotalItems,
	int ItemCount,
	int ItemsPerPage,
	int TotalPages,
	int CurrentPage
);

/// <summary>
///     Hypermedia links for the page, present only when a link context was supplied — see
///     <see cref="PaginatedResponse{T}.Links" />. An absent link (e.g. <see cref="Previous" /> on the first
///     page) is <see langword="null" /> and is serialized as <c>null</c> rather than dropped from the payload:
///     the value is the answer to "is there such a page", so the key stays and carries it.
/// </summary>
/// <param name="First">Link to page 1. Always present.</param>
/// <param name="Previous">Link to the preceding page, or <see langword="null" /> on page 1.</param>
/// <param name="Next">Link to the following page, or <see langword="null" /> on the last page and whenever nothing matched.</param>
/// <param name="Last">Link to the final page. Always present, and points at page 1 when nothing matched.</param>
public sealed record PaginatedLinks(
	string? First,
	string? Previous,
	string? Next,
	string? Last
);
