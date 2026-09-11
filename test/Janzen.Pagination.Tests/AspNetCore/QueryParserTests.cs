using Janzen.Pagination.AspNetCore;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>
///     Binding the six query-string inputs, and what happens to values that do not parse.
///     <para>
///         A claim about the <b>wire</b> grammar belongs here, reached through <c>ToPaginateQuery</c> — never
///         through a directly constructed <see cref="PaginateQuery" />. The two spellings differ: a value the
///         framework has already unescaped and trimmed is not the value a caller sent, so a rejection asserted
///         on a hand-built request can be a rejection the wire never reaches. That is how <c>limit=-1</c> stayed
///         unreachable over HTTP while every test of it passed.
///     </para>
/// </summary>
public sealed class QueryParserTests {

	private static PaginateQuery Parse(string queryString) {
		var context = new DefaultHttpContext();
		context.Request.QueryString = new QueryString(queryString);
		return context.Request.ToPaginateQuery();
	}

	private static Task<string> Rejects(PaginateQuery request) {
		return Assertions.RejectsAsync(() => TestData.Products().AsQueryable().PageAsync<ProductDto>(request));
	}

	[Fact]
	public void All_six_inputs_are_bound() {

		var query = Parse("?page=2&limit=25&sortBy=rank:DESC&search=acme&searchBy=name&filter.status=$eq:Active");

		Assert.Equal(2, query.Page);
		Assert.Equal(25, query.Limit);
		Assert.Equal(["rank:DESC"], query.SortBy);
		Assert.Equal("acme", query.Search);
		Assert.Equal(["name"], query.SearchBy);
		Assert.Equal(["$eq:Active"], Assert.Contains("status", query.Filters));

	}

	[Fact]
	public void Missing_inputs_fall_back_to_the_defaults() {

		var query = Parse("");

		Assert.Equal(1, query.Page);
		Assert.Null(query.Limit);
		Assert.Empty(query.SortBy);
		Assert.Null(query.Search);
		Assert.Empty(query.SearchBy);
		Assert.Empty(query.Filters);

	}

	[Fact]
	public void Unknown_parameters_are_ignored() {

		var query = Parse("?page=2&offset=40&utm_source=newsletter");

		Assert.Equal(2, query.Page);
		Assert.Empty(query.Filters);

	}

	[Fact]
	public void Repeated_sortBy_keeps_the_order_from_the_url() {
		Assert.Equal(["status:ASC", "rank:DESC"], Parse("?sortBy=status:ASC&sortBy=rank:DESC").SortBy);
	}

	[Fact]
	public void Filter_field_names_collapse_regardless_of_case() {

		var query = Parse("?filter.Status=$eq:Active&filter.status=$eq:Draft");

		// Counting the entries is not enough: one entry is also what a case-sensitive query collection would
		// produce, because the parser assigns rather than merges and the second spelling would then overwrite
		// the first. The reference sells this as "two criteria on one field rather than two fields", so both
		// values and their order are the assertion. Assert.Contains keeps the single-entry check implicitly.
		Assert.Equal(["$eq:Active", "$eq:Draft"], Assert.Contains("Status", query.Filters));

	}

	[Fact]
	public void A_filter_with_no_field_name_is_skipped() { Assert.Empty(Parse("?filter.=$eq:x").Filters); }

	[Fact]
	public void A_blank_search_term_is_treated_as_absent() { Assert.Null(Parse("?search=%20%20").Search); }

	[Fact]
	public void The_first_value_wins_for_the_single_valued_inputs() { Assert.Equal(2, Parse("?page=2&page=5").Page); }

	[Theory]
	[InlineData("?page=&page=2")]
	[InlineData("?page=%20&page=2")]
	public void A_blank_first_occurrence_does_not_shadow_a_real_page(string queryString) {
		// An HTML GET form emits every empty input, so the blank arrives first and a real value second.
		// sortBy and searchBy have always skipped blanks; the three single-valued readers now agree.
		Assert.Equal(2, Parse(queryString).Page);
	}

	[Fact]
	public void A_blank_first_occurrence_does_not_shadow_a_real_limit() { Assert.Equal(25, Parse("?limit=&limit=25").Limit); }

	[Fact]
	public void A_blank_first_occurrence_does_not_shadow_a_real_search_term() { Assert.Equal("widget", Parse("?search=&search=widget").Search); }

	[Fact]
	public void A_blank_first_occurrence_does_not_shadow_the_unlimited_literal() { Assert.Equal(-1, Parse("?limit=%20&limit=-1").Limit); }

	[Fact]
	public void A_padded_searchBy_field_name_is_trimmed() { Assert.Equal(["name"], Parse("?searchBy=%20name%20").SearchBy); }

	[Fact]
	public void A_padded_filter_field_name_is_trimmed() {
		Assert.Equal(["$eq:Active"], Assert.Contains("status", Parse("?filter.%20status%20=$eq:Active").Filters));
	}

	[Fact]
	public void A_filter_key_that_is_only_padding_is_skipped() { Assert.Empty(Parse("?filter.%20=$eq:x").Filters); }

	[Theory]
	[InlineData("?page=0")]
	[InlineData("?page=-1")]
	[InlineData("?page=abc")]
	[InlineData("?page=2.0")]
	[InlineData("?page=%2B5")]
	public async Task An_unparseable_page_is_carried_to_execution_as_a_400(string queryString) {
		// The binder cannot throw, so it records the error and the engine raises it when the query runs.
		Assert.Equal("Query parameter 'page' must be a positive integer.", await Rejects(Parse(queryString)));
	}

	[Fact]
	public async Task An_unparseable_limit_is_carried_to_execution_as_a_400() {
		Assert.Equal("Query parameter 'limit' must be a positive integer.", await Rejects(Parse("?limit=abc")));
	}

	[Theory]
	[InlineData("?page=")]
	[InlineData("?page=%20")]
	public void A_blank_page_is_not_an_error_it_is_simply_absent(string queryString) {
		Assert.Equal(1, Parse(queryString).Page);
	}

	[Fact]
	public void A_blank_limit_leaves_the_configured_default_in_force() { Assert.Null(Parse("?limit=").Limit); }

	[Fact]
	public void The_unlimited_limit_survives_binding() {

		// The binder has no configuration, so it cannot know whether this resource allows -1. Dropping it here
		// would make AllowUnlimited unreachable over HTTP while OpenAPI advertises it; the engine is where the
		// config-aware decision belongs, and it still refuses -1 for a resource that never opted in.
		Assert.Equal(-1, Parse("?limit=-1").Limit);

	}

	[Theory]
	[InlineData("?limit=-2")]
	[InlineData("?limit=-0")]
	[InlineData("?limit=%2B1")]   // an escaped plus, which a bare + would have decoded to a space
	[InlineData("?limit=%2B5")]
	[InlineData("?limit=abc")]
	[InlineData("?limit=1.0")]
	public async Task Every_other_malformed_limit_is_still_refused(string queryString) {
		Assert.Equal("Query parameter 'limit' must be a positive integer.", await Rejects(Parse(queryString)));
	}

	[Fact]
	public async Task A_bound_minus_one_is_answered_by_the_config_not_the_binder() {

		// Refused for a resource that did not opt in -- but with the engine's range message, which is what
		// proves the value reached it rather than dying during binding.
		string message = await Rejects(Parse("?limit=-1"));

		Assert.Equal("Query parameter 'limit' must be between 1 and 50.", message);

	}

	[Fact]
	public async Task A_bound_minus_one_pages_a_resource_that_opted_in() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(3, 50)
			.Sortable("id", p => p.Id)
			.DefaultSortBy("id")
			.WithTieBreaker(p => p.Id)
			.AllowUnlimited(100));

		var page = await TestData.Products().AsQueryable().PageAsync<ProductDto>(Parse("?limit=-1"), config);

		Assert.Equal(8, page.Items.Count);

	}

	[Fact]
	public async Task Limit_and_page_still_reject_the_same_forms() {

		// -1 is carved out of limit by matching the literal, not by loosening the number styles: AllowLeadingSign
		// would also have started accepting "+5" on limit while page went on rejecting it.
		Assert.Equal("Query parameter 'limit' must be a positive integer.", await Rejects(Parse("?limit=%2B5")));
		Assert.Equal("Query parameter 'page' must be a positive integer.", await Rejects(Parse("?page=%2B5")));

	}

	[Fact]
	public async Task Two_spellings_of_one_filter_field_are_refused_rather_than_one_being_dropped() {

		// Trimming the field name is what makes this reachable: the raw remainders were already unique under
		// IQueryCollection's own OrdinalIgnoreCase comparer, so nothing could collide before. Writing the
		// second over the first would answer 200 while silently discarding $eq:Active -- worse than the 400
		// the unconfigured ' status' used to get, because the caller is told nothing.
		var request = Parse("?filter.status=$eq:Active&filter.%20status=$eq:Draft");

		Assert.Equal("Filter for field 'status' is specified more than once.", await Rejects(request));

	}

	[Fact]
	public async Task A_page_error_still_outranks_a_duplicated_filter() {

		// The published order is page/limit, then filters, then search, then sortBy. The duplicate is held in
		// its own local for exactly this reason: sharing `error` would have let whichever ran first win.
		// A guard, not a fail-before case -- under the old last-wins there was no filter error to outrank.
		Assert.Equal(
			"Query parameter 'page' must be a positive integer.",
			await Rejects(Parse("?page=0&filter.status=$eq:Active&filter.%20status=$eq:Draft")));

	}

	/// <summary>
	///     The <b>engine's</b> paging guards outrank it too, which the binder alone cannot arrange: they run
	///     inside <c>Compose</c>, and the call that surfaces a binder error used to sit in front of all of them.
	///     Harmless while this channel could only hold a page or limit parse error — itself a paging error — and
	///     not harmless the moment a duplicated filter key could travel in it.
	/// </summary>
	[Fact]
	public async Task The_engine_s_paging_guards_outrank_a_duplicated_filter_too() {

		var guarded = PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 10, maxLimit: 50)
			.WithMaxOffset(100)
			.Sortable("id", p => p.Id)
			.WithTieBreaker(p => p.Id)
			.Filterable("status", p => p.Status, PaginateFilterOperator.Eq));

		const string Duplicated = "&filter.status=$eq:Active&filter.%20status=$eq:Draft";

		Assert.Equal(
			"Query parameter 'page' exceeds the allowed offset for this resource: at most 100 rows may be skipped.",
			await Assertions.RejectsAsync(() => TestData.Products().AsQueryable()
				.PageAsync<ProductDto>(Parse($"?page=1000&limit=10{Duplicated}"), guarded)));

		Assert.Equal(
			"Query parameter 'limit' must be between 1 and 50.",
			await Assertions.RejectsAsync(() => TestData.Products().AsQueryable()
				.PageAsync<ProductDto>(Parse($"?limit=9999{Duplicated}"), guarded)));

		// And the filter error is still reported once the paging ones are gone, rather than swallowed.
		Assert.Equal(
			"Filter for field 'status' is specified more than once.",
			await Assertions.RejectsAsync(() => TestData.Products().AsQueryable()
				.PageAsync<ProductDto>(Parse($"?page=1&limit=10{Duplicated}"), guarded)));

	}

}
