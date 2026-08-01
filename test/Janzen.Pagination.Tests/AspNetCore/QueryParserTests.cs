using Janzen.Pagination.AspNetCore;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>Binding the six query-string inputs, and what happens to values that do not parse.</summary>
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

		Assert.Single(query.Filters);

	}

	[Fact]
	public void A_filter_with_no_field_name_is_skipped() { Assert.Empty(Parse("?filter.=$eq:x").Filters); }

	[Fact]
	public void A_blank_search_term_is_treated_as_absent() { Assert.Null(Parse("?search=%20%20").Search); }

	[Fact]
	public void The_first_value_wins_for_the_single_valued_inputs() { Assert.Equal(2, Parse("?page=2&page=5").Page); }

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

}
