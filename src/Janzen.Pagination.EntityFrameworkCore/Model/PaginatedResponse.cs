namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     The pagination envelope: one page of <see cref="Items" /> plus paging <see cref="Meta" /> and, when a
///     link context was supplied, hypermedia <see cref="Links" />.
/// </summary>
/// <typeparam name="T">The item type of the page — the projection's result type, not the entity.</typeparam>
/// <param name="Items">The rows on this page, in the query's sort order. Empty past the last page.</param>
/// <param name="Meta">Paging counters for this page, plus the effective request echoed back: see <see cref="PaginatedMeta" />.</param>
/// <param name="Links">Hypermedia links, or <see langword="null" /> as a whole when the call supplied no link context. Serialized as <c>null</c>, never omitted.</param>
public sealed record PaginatedResponse<T>(
	IReadOnlyList<T> Items,
	PaginatedMeta Meta,
	PaginatedLinks? Links
) {

	/// <summary>
	///     Compares two envelopes by value: <see cref="Items" /> element by element (each through
	///     <typeparamref name="T" />'s own equality, so a projection record compares by value and a class by
	///     reference), then <see cref="Meta" /> and <see cref="Links" />. Written by hand because the synthesized
	///     version would compare the <see cref="Items" /> <b>list</b> by reference and report two identical pages
	///     as different.
	/// </summary>
	public bool Equals(PaginatedResponse<T>? other) {
		if (ReferenceEquals(this, other)) return true;

		return other is not null
			&& PaginateStructuralEquality.ListEquals(Items, other.Items)
			&& Meta == other.Meta
			&& Links == other.Links;
	}

	/// <summary>Hashes the same members <see cref="Equals(PaginatedResponse{T})" /> compares, so equal envelopes hash equal.</summary>
	public override int GetHashCode() {
		var hash = new HashCode();

		hash.Add(PaginateStructuralEquality.ListHash(Items));
		hash.Add(Meta);
		hash.Add(Links);

		return hash.ToHashCode();
	}

}

/// <summary>
///     Paging metadata: <see cref="TotalItems" /> across all pages, <see cref="ItemCount" /> on this page,
///     <see cref="ItemsPerPage" />, <see cref="TotalPages" /> and the 1-based <see cref="CurrentPage" /> —
///     plus <see cref="HasPreviousPage" /> / <see cref="HasNextPage" /> and the effective request echoed back
///     as <see cref="SortBy" />, <see cref="Search" />, <see cref="SearchBy" /> and <see cref="Filter" />.
/// </summary>
/// <remarks>
///     The engine fills the six non-positional members; the positional constructor leaves them at their
///     defaults, so a caller assembling this record by hand — composing a query and building its own envelope —
///     emits <c>"hasNextPage": false</c> and an empty echo unless it sets them with an object initializer. They
///     are settable rather than computed because a future counting strategy that caps or skips the count
///     derives <see cref="HasNextPage" /> from the fetch rather than from <see cref="TotalPages" />.
/// </remarks>
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
) {

	/// <summary>
	///     The ordering that was applied, in the request's own wire form (<c>"name:DESC"</c>) and in order. This is
	///     the <b>effective</b> order, not the echo of what arrived: an omitted <c>sortBy</c> reports the configured
	///     <c>DefaultSortBy</c>, which is the only way a client rendering sort arrows can know where they belong. The
	///     tie-breaker is not listed — it is an implementation detail of deterministic paging, not requested order.
	/// </summary>
	public IReadOnlyList<string> SortBy { get; init; } = [];

	/// <summary>The search term that was applied, or <see langword="null" /> when the request carried none. Serialized as <c>null</c> rather than dropped.</summary>
	public string? Search { get; init; }

	/// <summary>
	///     The searchable fields the term actually ran over — again effective, so an omitted <c>searchBy</c> reports
	///     every configured searchable field instead of an empty list the client would have to interpret. Empty when
	///     no search ran.
	/// </summary>
	public IReadOnlyList<string> SearchBy { get; init; } = [];

	/// <summary>The request's filters, echoed verbatim per field, for a client rendering filter chips. Empty when the request carried none.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filter { get; init; } = PaginateQuery.EmptyFilters;

	/// <summary>Whether a page precedes this one — <see cref="CurrentPage" /> is above 1. Saves every client re-deriving it from the counters.</summary>
	public bool HasPreviousPage { get; init; }

	/// <summary>Whether a page follows this one — <see cref="CurrentPage" /> is below <see cref="TotalPages" />. <see langword="false" /> past the last page, where nothing follows either.</summary>
	public bool HasNextPage { get; init; }

	/// <summary>
	///     Compares two metas by value, the three collection members included. Written by hand because the
	///     synthesized version compares <see cref="SortBy" />, <see cref="SearchBy" /> and <see cref="Filter" /> by
	///     <b>reference</b>, which would report two metas describing the same page as different.
	/// </summary>
	/// <remarks>
	///     <b>A member added to this record has to be added here and to <see cref="GetHashCode" /> too</b> — that is
	///     what a hand-written equality costs, and the compiler will not remind you.
	/// </remarks>
	public bool Equals(PaginatedMeta? other) {
		if (ReferenceEquals(this, other)) return true;

		return other is not null
			&& TotalItems == other.TotalItems
			&& ItemCount == other.ItemCount
			&& ItemsPerPage == other.ItemsPerPage
			&& TotalPages == other.TotalPages
			&& CurrentPage == other.CurrentPage
			&& HasPreviousPage == other.HasPreviousPage
			&& HasNextPage == other.HasNextPage
			&& Search == other.Search
			&& PaginateStructuralEquality.ListEquals(SortBy, other.SortBy)
			&& PaginateStructuralEquality.ListEquals(SearchBy, other.SearchBy)
			&& PaginateStructuralEquality.FilterEquals(Filter, other.Filter);
	}

	/// <summary>Hashes the same members <see cref="Equals(PaginatedMeta)" /> compares, so equal metas hash equal.</summary>
	public override int GetHashCode() {
		var hash = new HashCode();

		hash.Add(TotalItems);
		hash.Add(ItemCount);
		hash.Add(ItemsPerPage);
		hash.Add(TotalPages);
		hash.Add(CurrentPage);
		hash.Add(HasPreviousPage);
		hash.Add(HasNextPage);
		hash.Add(Search);
		hash.Add(PaginateStructuralEquality.ListHash(SortBy));
		hash.Add(PaginateStructuralEquality.ListHash(SearchBy));
		hash.Add(PaginateStructuralEquality.FilterHash(Filter));

		return hash.ToHashCode();
	}

}

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
) {

	/// <summary>
	///     Link to the page that was requested — the request echoed back, so it is never <see langword="null" /> and is
	///     present past the last page too, where <see cref="Next" /> and <see cref="Previous" /> already say what is
	///     navigable. A client that stores "where am I" URLs (bookmarks, retry, restoring table state) reads it here
	///     instead of reassembling it from <see cref="PaginatedResponse{T}.Meta" /> and its own knowledge of the path.
	///     Declared outside the positional list on purpose: the constructor, <c>Deconstruct</c> and <c>with</c> keep
	///     their shape, and it serializes after the four positional members.
	/// </summary>
	public string? Current { get; init; }

}

/// <summary>
///     Structural comparison for the envelope records' collection members. A record's synthesized equality runs
///     every field through <c>EqualityComparer&lt;T&gt;.Default</c>, which for a list or a dictionary is reference
///     equality — so two envelopes describing the same page would compare unequal. These restore what the record
///     shape advertises.
/// </summary>
internal static class PaginateStructuralEquality {

	public static bool ListEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) {
		if (ReferenceEquals(left, right)) return true;
		if (left.Count != right.Count) return false;

		var comparer = EqualityComparer<T>.Default;

		for (int index = 0; index < left.Count; index++) {
			if (!comparer.Equals(left[index], right[index])) return false;
		}

		return true;
	}

	public static bool FilterEquals(
		IReadOnlyDictionary<string, IReadOnlyList<string>> left,
		IReadOnlyDictionary<string, IReadOnlyList<string>> right
	) {

		if (ReferenceEquals(left, right)) return true;
		if (left.Count != right.Count) return false;

		// Keys are matched ORDINALLY, not through either dictionary's own comparer — those disagree. The model
		// binder builds an OrdinalIgnoreCase map, PaginateQuery.EmptyFilters is Ordinal, and a hand-built request
		// brings whatever the caller chose. Using the right-hand one would make equality asymmetric: a request
		// echoing 'Status' and one echoing 'status' would compare equal in one direction and not the other.
		// Ordinal is also the honest reading of the member, which echoes the request's field names verbatim.
		// The scan is quadratic in the number of filtered FIELDS, which the config caps in single digits.
		foreach ((string field, var values) in left) {

			bool matched = false;

			foreach ((string otherField, var otherValues) in right) {
				if (!string.Equals(field, otherField, StringComparison.Ordinal)) continue;

				matched = ListEquals(values, otherValues);
				break;
			}

			if (!matched) return false;

		}

		return true;

	}

	public static int ListHash<T>(IReadOnlyList<T> list) {
		var hash = new HashCode();

		foreach (var item in list) hash.Add(item);

		return hash.ToHashCode();
	}

	/// <summary>
	///     Hashes the filter map <b>commutatively</b>: a dictionary has no order, so two maps holding the same
	///     entries must hash the same however they were built. Per-entry hashes are therefore XORed rather than
	///     combined in sequence, and keys are hashed ordinally to match <see cref="FilterEquals" />.
	/// </summary>
	public static int FilterHash(IReadOnlyDictionary<string, IReadOnlyList<string>> filter) {

		int hash = filter.Count;

		foreach ((string field, var values) in filter) {
			hash ^= HashCode.Combine(StringComparer.Ordinal.GetHashCode(field), ListHash(values));
		}

		return hash;

	}

}
