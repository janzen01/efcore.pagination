using Janzen.Pagination.EntityFrameworkCore.Model;

namespace Janzen.Pagination.Tests.Support;

/// <summary>
///     Builders for the request shapes the tests repeat. <see cref="All" /> is the config's MaxLimit, so a
///     filter assertion sees every match rather than the first page of them.
/// </summary>
public static class Query {

	public const int All = 50;

	public static PaginateQuery Filter(string field, params string[] criteria) {
		return new PaginateQuery {
			Limit = All,
			Filters = new Dictionary<string, IReadOnlyList<string>> { [field] = criteria }
		};
	}

	public static PaginateQuery Filters(params (string Field, string Criterion)[] filters) {

		// Built rather than projected so the collection expression takes its type from the dictionary and
		// needs no cast to get one.
		Dictionary<string, IReadOnlyList<string>> map = [];
		foreach ((string field, string criterion) in filters) map[field] = [criterion];

		return new PaginateQuery { Limit = All, Filters = map };

	}

	public static PaginateQuery Sort(params string[] sortBy) { return new PaginateQuery { Limit = All, SortBy = sortBy }; }

	public static PaginateQuery Search(string? term, params string[] searchBy) {
		return new PaginateQuery { Limit = All, Search = term, SearchBy = searchBy };
	}

}
