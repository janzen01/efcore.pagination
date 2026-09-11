using System.Globalization;
using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The three registrations add-on packages make through <see cref="PaginateTypeSupport" />, none of which had a
///     direct test. The registry is process-wide and append-only, so everything here is keyed to types declared in
///     this file: another test can never reach them, and these registrations can never reach another test.
/// </summary>
public sealed class TypeSupportTests {

	public readonly record struct Sku(int Number);

	/// <summary>Filtered through a parser that signals bad input the documented way — by throwing.</summary>
	public readonly record struct Batch(int Number);

	/// <summary>Filtered through a parser that answers <see langword="null" />, which the contract never allows.</summary>
	public readonly record struct Lot(int Number);

	public sealed class Part {

		public int Id { get; set; }

		public Sku Sku { get; set; }

		public Batch Batch { get; set; }

		public Lot Lot { get; set; }

		public string Code { get; set; } = "";

	}

	public sealed record PartDto(int Id);

	public sealed record ConvertedPartDto(int Id, int Sku);

	public sealed record LeafPartDto(int Id, Sku Code);

	private readonly static PaginateConfig<Part> Config = PaginateConfig<Part>.Create(b => b
		.WithLimits(10, 10)
		.WithTieBreaker(p => p.Id)
		.Filterable("sku", p => p.Sku, PaginateFilterOperator.Eq)
		.Filterable("batch", p => p.Batch, PaginateFilterOperator.Eq)
		.Filterable("lot", p => p.Lot, PaginateFilterOperator.Eq));

	private static IQueryable<Part> Parts() {
		return new List<Part> {
			new() { Id = 1, Sku = new Sku(1), Code = "a" },
			new() { Id = 2, Sku = new Sku(2), Code = "b" }
		}.AsQueryable();
	}

	private static Task<PaginatedResponse<TResult>> PageAsync<TResult>(PaginateQuery request) {
		return Parts().PaginateAsync<Part, TResult>(request, Config, null, TestContext.Current.CancellationToken);
	}

	private static PaginateQuery Filter(string field, string criterion) {
		return new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> { [field] = [criterion] } };
	}

	[Fact]
	public async Task A_registered_parser_makes_a_custom_value_type_filterable() {

		PaginateTypeSupport.RegisterValueParser(typeof(Sku), value => new Sku(int.Parse(value, CultureInfo.InvariantCulture)));

		var page = await PageAsync<PartDto>(new PaginateQuery {
			Filters = new Dictionary<string, IReadOnlyList<string>> { ["sku"] = ["$eq:2"] }
		});

		Assert.Equal([2], page.Items.Select(item => item.Id));

	}

	[Fact]
	public async Task A_registered_conversion_projects_a_custom_type() {

		PaginateTypeSupport.RegisterProjectionConversion((source, target) =>
			source.Type == typeof(Sku) && target == typeof(int) ? Expression.Property(source, nameof(Sku.Number)) : null);

		var page = await PageAsync<ConvertedPartDto>(new PaginateQuery());

		Assert.Equal([1, 2], page.Items.Select(item => item.Sku));

	}

	[Fact]
	public async Task A_registered_simple_type_is_a_leaf_the_projection_does_not_recurse_into() {

		PaginateTypeSupport.RegisterSimpleType(typeof(Sku));

		// Code is a string and the DTO wants a Sku, so this is unprojectable either way — registering the type is
		// what makes the engine say so, instead of hunting System.String for a member called Number.
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PageAsync<LeafPartDto>(new PaginateQuery()));

		Assert.Equal("Cannot automatically project 'Part.Code' from 'String' to 'Sku'.", exception.Message);

	}

	[Fact]
	public async Task A_registered_parser_that_throws_answers_the_documented_400() {

		// The shape the guide teaches and the repository's own registrations all use: the parser throws for text
		// it cannot read. The registry is consulted *before* the converter's translation try, so this escaped as
		// an unhandled FormatException -- a 500 from a query string, where the same malformed value on a built-in
		// type has always been a 400. Registering an override for a built-in type downgraded it the same way.
		PaginateTypeSupport.RegisterValueParser(typeof(Batch), value => new Batch(int.Parse(value, CultureInfo.InvariantCulture)));

		var exception = await Assert.ThrowsAsync<PaginateQueryException>(() => PageAsync<PartDto>(Filter("batch", "$eq:nope")));

		Assert.Equal("Value 'nope' is not valid for 'batch'.", exception.Message);

	}

	[Fact]
	public async Task A_registered_parser_answering_null_is_the_same_400() {

		// Func<string, object?> invites it, but null is not an answer the contract allows: against a non-nullable
		// target it reached Expression.Constant(null, typeof(Lot)) and left as an ArgumentException -- again a 500.
		PaginateTypeSupport.RegisterValueParser(typeof(Lot), _ => null);

		var exception = await Assert.ThrowsAsync<PaginateQueryException>(() => PageAsync<PartDto>(Filter("lot", "$eq:2")));

		Assert.Equal("Value '2' is not valid for 'lot'.", exception.Message);

	}

}
