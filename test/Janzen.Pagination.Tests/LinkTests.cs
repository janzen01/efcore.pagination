using Janzen.Pagination.EntityFrameworkCore.Links;

using System.Text.Json;

namespace Janzen.Pagination.Tests;

/// <summary>Navigation links, which appear only when a <see cref="PaginateLinkContext" /> is supplied.</summary>
public sealed class LinkTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>
	///     <c>utm_source</c> is here because the binder does not recognise it: the documented promise is that
	///     <b>every</b> current parameter except <c>page</c> is carried over, unrecognised ones included, so
	///     client-side state survives paging. Without such a key the drop predicate could be narrowed to the six
	///     known parameters and every link assertion would still pass.
	/// </summary>
	private readonly static PaginateLinkContext Context = new("/products", [
		new KeyValuePair<string, string>("limit", "3"),
		new KeyValuePair<string, string>("filter.status", "$eq:Active"),
		new KeyValuePair<string, string>("utm_source", "news"),
		new KeyValuePair<string, string>("page", "2")
	]);

	/// <summary>The serializer defaults ASP.NET Core applies, so the asserted JSON is the one clients see.</summary>
	private readonly static JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	private async Task<PaginatedResponse<ProductDto>> PageFor(int page, PaginateLinkContext? context) {
		await using var dbContext = fixture.CreateContext();
		return await SqliteFixture.Products(dbContext).PageAsync<ProductDto>(new PaginateQuery { Page = page }, linkContext: context);
	}

	private async Task<PaginatedLinks> LinksFor(int page, PaginateLinkContext context) {

		var result = await this.PageFor(page, context);

		Assert.NotNull(result.Links);

		return result.Links;

	}

	[Fact]
	public async Task Without_a_context_there_are_no_links() {

		var page = await this.PageFor(1, null);

		Assert.Null(page.Links);

		// Meta stays truthful: it is what a caller outside ASP.NET Core navigates by, via PaginateQuery.WithPage.
		Assert.Equal(1, page.Meta.CurrentPage);
		Assert.Equal(3, page.Meta.TotalPages);

	}

	[Fact]
	public async Task Without_a_context_links_is_serialized_as_null() {
		Assert.Contains("\"links\":null", JsonSerializer.Serialize(await this.PageFor(1, null), WebJson));
	}

	[Fact]
	public async Task An_absent_link_is_serialized_as_null_rather_than_dropped() {

		// The last page has no next. That null is the answer the client asked for, so the key has to carry it —
		// a missing key would make "no next page" indistinguishable from "this API has no next link".
		string json = JsonSerializer.Serialize(await this.PageFor(3, Context), WebJson);

		Assert.Contains("\"next\":null", json);

	}

	[Fact]
	public async Task Other_query_parameters_are_carried_over_and_escaped_while_page_is_replaced() {
		Assert.Equal("/products?limit=3&filter.status=%24eq%3AActive&utm_source=news&page=1", (await this.LinksFor(2, Context)).First);
	}

	[Fact]
	public async Task A_middle_page_links_in_both_directions() {

		var links = await this.LinksFor(2, Context);

		Assert.EndsWith("page=1", links.Previous);
		Assert.EndsWith("page=3", links.Next);
		Assert.EndsWith("page=3", links.Last);

	}

	[Fact]
	public async Task The_first_page_has_no_previous() {

		var links = await this.LinksFor(1, Context);

		Assert.Null(links.Previous);
		Assert.EndsWith("page=2", links.Next);

	}

	[Fact]
	public async Task The_last_page_has_no_next() {

		var links = await this.LinksFor(3, Context);

		Assert.EndsWith("page=2", links.Previous);
		Assert.Null(links.Next);

	}

	[Fact]
	public async Task Links_are_relative_to_the_path_with_no_scheme_or_host() {
		Assert.StartsWith("/products?", (await this.LinksFor(1, Context)).First);
	}

	[Fact]
	public void A_path_that_was_never_escaped_is_refused() {

		// "/t/x?y/products?page=1" — the query string becomes "y/products?page=1", so the link addresses a
		// different resource with no page at all. A space produces an invalid URI-reference instead.
		Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/x?y/products", []));
		Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/a b/products", []));
		Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/a#b/products", []));

	}

	/// <summary>
	///     A control character is refused for the reason the engine strips them out of a 400's message: the links
	///     are written into an opt-in <c>Link</c> response header, and a CR or LF there splits the header. The
	///     guard rejected a space but not a newline, which is the inconsistent half of the same rule.
	/// </summary>
	[Fact]
	public void A_path_carrying_a_control_character_is_refused() {

		Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/a\rb/products", []));
		Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/a\nb/products", []));
		Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/a\u007Fb/products", []));

		// The offending character is named by code point rather than echoed: a message carrying a raw CR is the
		// thing the guard exists to stop.
		var refused = Assert.Throws<ArgumentException>(() => new PaginateLinkContext("/t/a\rb/products", []));
		Assert.Contains("U+000D", refused.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("\r", refused.Message, StringComparison.Ordinal);

	}

	/// <summary>
	///     <c>with</c> runs the same guard construction does. A record's synthesized copy constructor copies every
	///     field verbatim, so a guard kept in a field initializer is carried across as "already validated" and the
	///     member the caller actually changed is never looked at — the shape that let an unescaped path through.
	/// </summary>
	[Fact]
	public void A_with_expression_cannot_walk_past_the_path_guard() {

		var context = new PaginateLinkContext("/products", []);

		Assert.Throws<ArgumentException>(() => context with { Path = "/t/a b/products" });
		Assert.Throws<ArgumentException>(() => context with { Path = "/t/x?y/products" });
		Assert.Throws<ArgumentException>(() => context with { Path = "/t/a\nb/products" });
		Assert.Throws<ArgumentNullException>(() => context with { QueryParameters = null! });

		// A valid one still copies, so the guard did not cost the record its `with`.
		Assert.Equal("/other", (context with { Path = "/other" }).Path);

	}

	[Fact]
	public void An_escaped_path_and_raw_parameters_are_what_the_record_asks_for() {

		// The path is pre-escaped, the parameters are raw: the builder percent-encodes only the latter.
		var context = new PaginateLinkContext("/t/a%20b/products", [new KeyValuePair<string, string>("filter.status", "$eq:Active")]);

		Assert.Equal("/t/a%20b/products", context.Path);

	}

	[Fact]
	public void A_null_path_or_parameter_list_is_refused_at_construction() {

		// Both used to fault later, inside the builder, via Uri.EscapeDataString. The ParamName is asserted
		// too: left to CallerArgumentExpression it reports the lowercase positional parameter, while the
		// path-character throw beside it says "Path" -- one constructor naming one argument two ways.
		var path = Assert.Throws<ArgumentNullException>(() => new PaginateLinkContext(null!, []));
		var parameters = Assert.Throws<ArgumentNullException>(() => new PaginateLinkContext("/products", null!));

		Assert.Equal(nameof(PaginateLinkContext.Path), path.ParamName);
		Assert.Equal(nameof(PaginateLinkContext.QueryParameters), parameters.ParamName);

	}

	[Fact]
	public void Two_contexts_describing_the_same_request_compare_equal() {

		// The record shape advertises value equality; the synthesized version compared QueryParameters by
		// reference, so a consumer testing their own "request -> context" factory with Assert.Equal never matched.
		var left = new PaginateLinkContext("/products", [
			new KeyValuePair<string, string>("limit", "3"),
			new KeyValuePair<string, string>("filter.status", "$eq:Active")
		]);

		var right = new PaginateLinkContext("/products", [
			new KeyValuePair<string, string>("limit", "3"),
			new KeyValuePair<string, string>("filter.status", "$eq:Active")
		]);

		Assert.Equal(left, right);
		Assert.Equal(left.GetHashCode(), right.GetHashCode());

	}

	[Fact]
	public void Parameter_order_is_part_of_the_value() {

		// Order decides the emitted link, so two orderings really are two different contexts.
		var left = new PaginateLinkContext("/products", [
			new KeyValuePair<string, string>("a", "1"),
			new KeyValuePair<string, string>("b", "2")
		]);

		var right = new PaginateLinkContext("/products", [
			new KeyValuePair<string, string>("b", "2"),
			new KeyValuePair<string, string>("a", "1")
		]);

		Assert.NotEqual(left, right);

	}

	[Fact]
	public void A_different_path_or_a_different_value_is_a_different_context() {

		var context = new PaginateLinkContext("/products", [new KeyValuePair<string, string>("limit", "3")]);

		Assert.NotEqual(context, new PaginateLinkContext("/orders", [new KeyValuePair<string, string>("limit", "3")]));
		Assert.NotEqual(context, new PaginateLinkContext("/products", [new KeyValuePair<string, string>("limit", "4")]));
		Assert.NotEqual(context, new PaginateLinkContext("/products", [new KeyValuePair<string, string>("page", "3")]));
		Assert.NotEqual(context, new PaginateLinkContext("/products", []));

	}

}
