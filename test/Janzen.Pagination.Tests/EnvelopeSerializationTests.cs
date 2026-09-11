using System.Text.Json;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The envelope's JSON member names are the library's own published contract — the guide and all four
///     package READMEs print them — so they may not depend on the host's
///     <c>JsonSerializerOptions.PropertyNamingPolicy</c>. These run the same envelope through the web defaults
///     and through a host that sets no policy at all, and require the identical documented shape from both.
/// </summary>
public sealed class EnvelopeSerializationTests {

	private readonly static string[] MetaMembers = [
		"totalItems", "itemCount", "itemsPerPage", "totalPages", "currentPage",
		"sortBy", "search", "searchBy", "filter", "hasPreviousPage", "hasNextPage"
	];

	private readonly static string[] LinkMembers = ["first", "previous", "next", "last", "current"];

	private static PaginatedResponse<ProductDto> Envelope() {

		var meta = new PaginatedMeta(37, 2, 2, 19, 2) {
			SortBy = ["name:DESC"],
			Search = null,
			SearchBy = [],
			HasPreviousPage = true,
			HasNextPage = true
		};

		var links = new PaginatedLinks("/p?page=1", "/p?page=1", "/p?page=3", "/p?page=19") { Current = "/p?page=2" };

		return new PaginatedResponse<ProductDto>([], meta, links);

	}

	private static void AssertDocumentedShape(JsonSerializerOptions options) {

		using var document = JsonDocument.Parse(JsonSerializer.Serialize(Envelope(), options));
		var root = document.RootElement;

		Assert.Equal(["items", "meta", "links"], root.EnumerateObject().Select(member => member.Name));
		Assert.Equal(MetaMembers, root.GetProperty("meta").EnumerateObject().Select(member => member.Name));
		Assert.Equal(LinkMembers, root.GetProperty("links").EnumerateObject().Select(member => member.Name));

	}

	[Fact]
	public void The_documented_shape_holds_under_the_web_defaults() {
		AssertDocumentedShape(new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}

	[Fact]
	public void The_documented_shape_holds_when_the_host_sets_no_naming_policy() {
		// An ASP.NET Core application that assigns PropertyNamingPolicy = null -- or one hosting the envelope
		// outside MVC, where the default really is null -- used to serve "Items" and "Meta.TotalItems".
		AssertDocumentedShape(new JsonSerializerOptions { PropertyNamingPolicy = null });
	}

	[Fact]
	public void The_documented_shape_holds_under_a_foreign_naming_policy() {
		AssertDocumentedShape(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
	}

	[Fact]
	public void The_documented_shape_round_trips() {

		var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
		string json = JsonSerializer.Serialize(Envelope(), options);

		Assert.Equal(Envelope(), JsonSerializer.Deserialize<PaginatedResponse<ProductDto>>(json, options));

	}

}
