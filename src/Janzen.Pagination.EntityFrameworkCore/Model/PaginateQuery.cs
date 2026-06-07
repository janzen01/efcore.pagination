using System.Collections.ObjectModel;

namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     An immutable pagination request. Bind it from a query string (ASP.NET integration) or construct it directly
///     with an object initializer for non-web callers (gRPC, console, tests), e.g.
///     <c>new PaginateQuery { Page = 2, Limit = 25, SortBy = ["name:DESC"] }</c>. Out-of-range values are
///     clamped/validated by the engine when the query is executed.
/// </summary>
public sealed class PaginateQuery {

	public const int DefaultPage = 1;

	internal readonly static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyFilters =
		new ReadOnlyDictionary<string, IReadOnlyList<string>>(
			new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

	public int Page { get; init; } = DefaultPage;

	public int? Limit { get; init; }

	public IReadOnlyList<string> SortBy { get; init; } = [];

	public string? Search { get; init; }

	public IReadOnlyList<string> SearchBy { get; init; } = [];

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; } = EmptyFilters;

	/// <summary>Parse-time validation error captured during model binding; surfaced as a 400 on execution.</summary>
	internal string? ValidationError { get; init; }

	/// <summary>Throws the captured parse-time validation error, if any.</summary>
	internal void EnsureValid() {
		if (ValidationError is not null) throw new PaginateQueryException(ValidationError);
	}

}
