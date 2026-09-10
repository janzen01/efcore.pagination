using Microsoft.EntityFrameworkCore;

namespace Janzen.Pagination.Tests;

/// <summary>
///     Every filter and search value reaches the database as a SQL parameter rather than an inlined literal —
///     the library's single most consequential SQL-generation decision, and a published one:
///     <c>reference/query-string/</c> promises "one cached plan, whatever the list contains".
///     <para>
///         It had no test. Reducing <c>ToDatabaseParameter</c> to <c>return value;</c> — the one line that
///         disarms all four wrap sites — kept the whole suite green while every value became a literal, because
///         nothing asserts on the emitted SQL except the composer's own composed-equals-executed comparison,
///         where both sides move together. The symptom on a consumer's server is not an error but a plan cache
///         growing one entry per distinct value.
///     </para>
/// </summary>
public sealed class ParameterisationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>
	///     The composed statement without <c>ToQueryString</c>'s <c>.param set</c> preamble. Stripping it is what
	///     makes "the value does not appear" mean the statement body rather than the parameter listing, which
	///     carries the value under either implementation.
	/// </summary>
	private string StatementFor(PaginateQuery request) {

		using var context = fixture.CreateContext();

		string queryString = SqliteFixture.Products(context).ApplyPaginateFilters(request, TestData.Config).Query.ToQueryString();

		return string.Join(' ', queryString
			.Split('\n')
			.Where(line => !line.TrimStart().StartsWith(".param", StringComparison.Ordinal))
			.SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

	}

	[Fact]
	public void An_equality_filter_compares_against_a_parameter_not_a_literal() {

		string sql = this.StatementFor(Query.Filter("name", "$eq:Widget"));

		Assert.Contains("\"Name\" = @", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("'Widget'", sql, StringComparison.Ordinal);

	}

	[Fact]
	public void An_in_filter_expands_one_parameter_rather_than_a_literal_list() {

		// The whole point of the wrapper for $in: without it the list is inlined as IN (1, 2, 3), so every
		// distinct combination of values compiles its own plan.
		string sql = this.StatementFor(Query.Filter("id", "$in:1,2,3"));

		Assert.Contains("json_each(@", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("IN (1, 2, 3)", sql, StringComparison.Ordinal);

	}

	[Theory]
	[InlineData("filter")]
	[InlineData("search")]
	public void A_pattern_is_a_parameter_on_both_the_filter_and_the_search_path(string path) {

		// The ESCAPE clause is deliberately not asserted here: it is a structural constant emitted identically
		// with and without the wrapper, so it does not discriminate. LikeStrategyTests already pins it.
		string sql = this.StatementFor(path == "filter" ? Query.Filter("name", "$ilike:wid") : Query.Search("wid"));

		Assert.Contains("LIKE @", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("'%wid%'", sql, StringComparison.Ordinal);

	}

}
