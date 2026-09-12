namespace Janzen.Pagination.Tests;

/// <summary>
///     The <c>filter.&lt;field&gt;=[$not:][$and:|$or:]$op[:value]</c> grammar: what parses, what is rejected,
///     and how repeated criteria on one field combine.
/// </summary>
public sealed class FilterGrammarTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private async Task<string> Rejects(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request));
	}

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request);
	}

	[Fact]
	public async Task Unknown_operator_is_rejected() {
		Assert.Equal("Filter 'name' uses unknown operator '$foo'.", await this.Rejects(Query.Filter("name", "$foo:x")));
	}

	[Fact]
	public async Task Value_without_an_operator_is_rejected() {
		Assert.Equal("Filter 'name' must use the format '$operator:value'.", await this.Rejects(Query.Filter("name", "Widget")));
	}

	[Fact]
	public async Task Operator_without_a_value_is_rejected() {
		Assert.Equal("Filter 'name' must use the format '$operator:value'.", await this.Rejects(Query.Filter("name", "$eq")));
	}

	[Fact]
	public async Task Empty_criterion_is_rejected() {
		Assert.Equal("Filter 'name' must not be empty.", await this.Rejects(Query.Filter("name", "")));
	}

	[Fact]
	public async Task Unknown_field_is_rejected() {
		Assert.Equal("Filter for field 'nope' is not configured.", await this.Rejects(Query.Filter("nope", "$eq:x")));
	}

	[Fact]
	public async Task Operator_not_granted_for_the_field_is_rejected() {
		Assert.Equal("Filter 'rank' does not support operator '$ilike'.", await this.Rejects(Query.Filter("rank", "$ilike:x")));
	}

	[Fact]
	public async Task Null_is_the_one_operator_allowed_without_a_value() {
		Assertions.HasIds(await this.Page(Query.Filter("discontinuedAt", "$null")), 1, 2, 3, 4, 5, 7, 8);
	}

	[Theory]
	[InlineData("$null:false")]
	[InlineData("$null:true")]
	[InlineData("$null:")]
	public async Task Null_is_also_the_one_operator_that_refuses_a_value(string criterion) {
		// The value used to be parsed and then dropped, so `$null:false` selected the rows it says it excludes.
		// The bare trailing colon was tolerated beside it, which is the same mistake read one character earlier.
		Assert.Equal("Filter 'discontinuedAt' does not take a value for '$null'.", await this.Rejects(Query.Filter("discontinuedAt", criterion)));
	}

	[Fact]
	public async Task Everything_after_the_operator_colon_is_the_value() {
		// The value itself contains a colon; parsing stops at the first operator token and takes the rest.
		Assertions.HasIds(await this.Page(Query.Filter("name", "$eq:Doohickey: legacy")), 6);
	}

	[Fact]
	public async Task Operator_tokens_are_case_insensitive() {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$EQ:Draft")), 3, 5);
	}

	[Fact]
	public async Task Field_names_are_case_insensitive() {
		Assertions.HasIds(await this.Page(Query.Filter("STATUS", "$eq:Draft")), 3, 5);
	}

	[Fact]
	public async Task Not_negates_the_criterion() {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$not:$eq:Active")), 3, 5, 6);
	}

	[Fact]
	public async Task Criteria_on_one_field_default_to_and() {
		Assertions.HasIds(await this.Page(Query.Filter("rank", "$gte:20", "$lte:40")), 2, 3, 4);
	}

	[Fact]
	public async Task Or_joins_criteria_on_one_field() {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft", "$or:$eq:Discontinued")), 3, 5, 6);
	}

	[Fact]
	public async Task And_can_be_written_explicitly() {
		Assertions.HasIds(await this.Page(Query.Filter("rank", "$gte:20", "$and:$lte:40")), 2, 3, 4);
	}

	[Theory]
	[InlineData("$or:$not:$eq:Active")]
	[InlineData("$not:$or:$eq:Active")]
	public async Task Prefixes_may_come_in_either_order(string second) {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft", second)), 3, 5, 6);
	}

	[Fact]
	public async Task Different_fields_are_always_joined_with_and() {
		Assertions.HasIds(await this.Page(Query.Filters(("status", "$eq:Active"), ("rank", "$gt:50"))), 7, 8);
	}

	[Theory]
	[InlineData("$or")]
	[InlineData("$and")]
	public async Task A_connector_on_the_first_criterion_is_rejected(string connector) {

		// A connector says how a criterion joins the one before it, so on the first one it has nothing to join
		// to. It used to be read and then discarded.
		Assert.Equal($"Filter 'status' must not begin with '{connector}'; a connector joins a criterion to the one before it.",
			await this.Rejects(Query.Filter("status", $"{connector}:$eq:Draft")));

	}

	[Fact]
	public async Task A_uniformly_or_prefixed_filter_no_longer_ands_silently() {

		// The shape a client that always prefixes $or: sends. Every field's leading connector was discarded and
		// fields are always joined with AND, so two criteria a caller meant as alternatives answered an empty
		// page -- a wrong result set, with nothing in the response to say why.
		Assert.Equal("Filter 'status' must not begin with '$or'; a connector joins a criterion to the one before it.",
			await this.Rejects(Query.Filters(("status", "$or:$eq:Draft"), ("name", "$or:$eq:Widget"))));

	}

	[Fact]
	public async Task A_connector_is_rejected_on_every_field_it_leads() {

		// Cross-field too: each field's criteria are read from their own first criterion, and the second field's
		// leading connector was discarded just as silently.
		Assert.Equal("Filter 'rank' must not begin with '$or'; a connector joins a criterion to the one before it.",
			await this.Rejects(Query.Filters(("status", "$eq:Active"), ("rank", "$or:$gt:50"))));

	}

	[Fact]
	public async Task A_connector_still_reads_after_a_negation_on_a_later_criterion() {
		// Guard for the rejection above: it must fire on position, not on the modifier appearing at all.
		Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft", "$not:$or:$eq:Active")), 3, 5, 6);
	}

	[Fact]
	public async Task A_list_operator_no_longer_trims_its_entries() {

		// One padding character used to mean two different values: $in trimmed its entries and $eq did not,
		// so the same text matched through one operator and not the other. They now agree.
		var listed = await this.Page(Query.Filter("name", "$in: Widget"));
		var single = await this.Page(Query.Filter("name", "$eq: Widget"));

		Assert.Empty(single.Items);
		Assert.Equal(single.Items.Count, listed.Items.Count);

	}

	[Fact]
	public async Task Two_keys_resolving_to_one_field_are_rejected() {

		// Field lookup is case-insensitive but a hand-built Filters map need not be: an ordinal dictionary --
		// the type's own default, and what the documented non-web construction path produces -- carries both
		// 'status' and 'Status'. They then AND two criteria on one field and the page comes back empty with no
		// error at all. Unreachable over HTTP, where the binder collapses the keys before the engine sees them.
		var request = Query.Filters(("status", "$eq:Active"), ("Status", "$eq:Draft"));

		Assert.Equal("Filter for field 'Status' repeats 'status'; combine the criteria in one entry.", await this.Rejects(request));

	}

	[Fact]
	public async Task An_unpadded_entry_beside_a_padded_one_still_matches() {
		// Only the padded entry stops matching; the separator itself is unchanged.
		Assertions.HasIds(await this.Page(Query.Filter("name", "$in:Widget, Gizmo")), 1);
	}

	[Fact]
	public async Task A_padded_entry_on_a_numeric_field_still_parses() {
		// Not a regression — a guard. Dropping the split's trim leaves the padding on the entry, and it is the
		// numeric styles (AllowLeadingWhite / AllowTrailingWhite) that absorb it. Narrow those and this breaks.
		Assertions.HasIds(await this.Page(Query.Filter("rank", "$btw:20 , 40")), 2, 3, 4);
	}

	[Fact]
	public async Task One_key_per_field_is_still_how_several_criteria_are_expressed() {
		// The migration the rejection above points at, and proof it rejects duplicates rather than repetition.
		Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft", "$or:$eq:Discontinued")), 3, 5, 6);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(100)]
	[InlineData(1600)]
	public async Task Repeated_modifiers_parse_to_the_same_criterion(int prefixes) {
		// $not does not accumulate -- it is a flag, not a toggle -- so any number of them means the same thing.
		// Locked at 1 600 as well, which is the count that fits one 8 KB request line: the walk over them is a
		// span slice now, and this is the behaviour that must survive anyone rewriting it back.
		string value = string.Concat(Enumerable.Repeat("$not:", prefixes)) + "$eq:Active";
		Assertions.HasIds(await this.Page(Query.Filter("status", value)), 3, 5, 6);
	}

	[Fact]
	public async Task Repeated_modifiers_do_not_make_parsing_cost_quadratic() {
		// A guard, not a benchmark. Re-slicing the value as a string per modifier copied its whole tail each
		// time, so 1 600 repeated prefixes -- one 8 KB request line -- allocated 12.9 MB, an amplification of
		// ~1 612x over the bytes the client sent. Slicing a span allocates nothing, and the measured ratio is
		// 1.0; the bound below is deliberately loose enough to survive a runtime's own noise and still fail
		// hard on a return to string slicing, which scored 4 478x here.
		await using var context = fixture.CreateContext();
		var products = SqliteFixture.Products(context);

		// Measured through the public composer rather than the parser: ApplyPagination builds the query and
		// stops before executing it, so the loop is pure composition and the quadratic term still dominates
		// everything else it does.
		long Allocated(string value) {
			var request = Query.Filter("status", value);
			products.ApplyPagination(request, TestData.Config);   // JIT the path first
			long before = GC.GetAllocatedBytesForCurrentThread();
			for (int i = 0; i < 20; i++) products.ApplyPagination(request, TestData.Config);
			return GC.GetAllocatedBytesForCurrentThread() - before;
		}

		long one = Allocated("$not:$eq:Active");
		long many = Allocated(string.Concat(Enumerable.Repeat("$not:", 1600)) + "$eq:Active");

		Assert.True(many < one * 50, $"1 600 modifiers allocated {many} bytes against {one} for one: {(double)many / one:F1}x");
	}

}
