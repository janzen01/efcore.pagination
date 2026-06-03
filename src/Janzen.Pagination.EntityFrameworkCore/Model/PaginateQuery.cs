using System.Collections.ObjectModel;

namespace Janzen.Pagination.EntityFrameworkCore.Model;

public sealed class PaginateQuery {

	public const int DefaultPage = 1;

	internal readonly static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyFilters =
		new ReadOnlyDictionary<string, IReadOnlyList<string>>(
			new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

	public int Page { get; internal init; } = DefaultPage;

	public int? Limit { get; internal init; }

	public IReadOnlyList<string> SortBy { get; internal init; } = [];

	public string? Search { get; internal init; }

	public IReadOnlyList<string> SearchBy { get; internal init; } = [];

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; internal init; } = EmptyFilters;

	/// <summary>Parse-time validation error captured during model binding; surfaced as a 400 on execution.</summary>
	internal string? ValidationError { get; init; }

	/// <summary>Throws the captured parse-time validation error, if any.</summary>
	internal void EnsureValid() {
		if (ValidationError is not null) throw new PaginateQueryException(ValidationError);
	}

}
