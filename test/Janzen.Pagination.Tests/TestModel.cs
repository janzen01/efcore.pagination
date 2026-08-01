using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Model;

namespace Janzen.Pagination.Tests;

public enum ProductStatus {

	Draft,
	Active,
	Discontinued

}

public sealed class Product {

	public int Id { get; set; }

	public string Name { get; set; } = "";

	public string? Description { get; set; }

	public ProductStatus Status { get; set; }

	/// <summary>Ordering and range assertions use this, never <see cref="Price" /> — SQLite compares decimals lexically.</summary>
	public int Rank { get; set; }

	public decimal Price { get; set; }

	public bool IsFeatured { get; set; }

	public Guid ExternalId { get; set; }

	public DateTimeOffset CreatedAt { get; set; }

	public DateTimeOffset? DiscontinuedAt { get; set; }

	public List<string> Tags { get; set; } = [];

	public int? CategoryId { get; set; }

	public Category? Category { get; set; }

	public List<Review> Reviews { get; set; } = [];

}

public sealed class Category {

	public int Id { get; set; }

	public string Name { get; set; } = "";

	public List<Product> Products { get; set; } = [];

}

public sealed class Review {

	public int Id { get; set; }

	public int ProductId { get; set; }

	public string Reviewer { get; set; } = "";

	public int Rating { get; set; }

}

public sealed record ProductDto(int Id, string Name, ProductStatus Status, int Rank);

public sealed record CategoryDto(int Id, string Name);

public sealed record ProductWithCategoryDto(int Id, string Name, CategoryDto? Category);

public sealed record ReviewDto(int Id, string Reviewer, int Rating);

public sealed record ProductSummary(int Id, string Name, int ReviewCount, List<ReviewDto> Reviews);

/// <summary>Deliberately unprojectable: <c>Name</c> is a string on the entity.</summary>
public sealed record UnprojectableDto(int Id, int Name);

/// <summary>Deliberately unprojectable: no <c>Nonexistent</c> member on the entity.</summary>
public sealed record MissingMemberDto(int Id, string Nonexistent);

/// <summary>Deliberately unprojectable: nullable <c>Category</c> into a non-nullable parameter.</summary>
public sealed record NonNullableCategoryDto(int Id, CategoryDto Category);

public static class TestData {

	public readonly static DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Deterministic ids so a Guid filter can be written as a literal.</summary>
	public static Guid ExternalId(int id) { return new Guid($"00000000-0000-0000-0000-00000000000{id}"); }

	/// <summary>
	///     Eight products, shared by both legs. Names carry literal <c>%</c> and <c>_</c> so pattern escaping is
	///     testable, and <c>APPLE</c>/<c>apple pie</c> differ only in case. <c>Rank</c> is a stable ordering key.
	/// </summary>
	public static List<Product> Products() {

		var electronics = new Category { Id = 1, Name = "Electronics" };
		var toys = new Category { Id = 2, Name = "Toys" };
		var food = new Category { Id = 3, Name = "Food" };

		List<Product> products = [
			new Product {
				Id = 1, Name = "Widget", Description = "a basic widget", Status = ProductStatus.Active,
				Rank = 10, Price = 9.99m, CreatedAt = Epoch.AddDays(1), Tags = ["red", "small"],
				CategoryId = 1, Category = electronics,
				Reviews = [
					new Review { Id = 1, ProductId = 1, Reviewer = "ann", Rating = 5 },
					new Review { Id = 2, ProductId = 1, Reviewer = "bob", Rating = 3 },
					new Review { Id = 3, ProductId = 1, Reviewer = "cid", Rating = 4 }
				]
			},
			new Product {
				Id = 2, Name = "Wid-gadget", Description = null, Status = ProductStatus.Active,
				Rank = 20, Price = 19.99m, CreatedAt = Epoch.AddDays(2), Tags = ["red", "large"],
				CategoryId = 1, Category = electronics,
				Reviews = [new Review { Id = 4, ProductId = 2, Reviewer = "ann", Rating = 2 }]
			},
			new Product {
				Id = 3, Name = "Gizmo", Description = "shiny gizmo", Status = ProductStatus.Draft,
				Rank = 30, Price = 29.99m, CreatedAt = Epoch.AddDays(3), Tags = ["blue"],
				CategoryId = 2, Category = toys
			},
			new Product {
				Id = 4, Name = "50% off bundle", Description = "discounted", Status = ProductStatus.Active,
				Rank = 40, Price = 5.00m, CreatedAt = Epoch.AddDays(4), Tags = [],
				CategoryId = 2, Category = toys
			},
			new Product {
				Id = 5, Name = "a_b_c", Description = null, Status = ProductStatus.Draft,
				Rank = 50, Price = 1.00m, CreatedAt = Epoch.AddDays(5), Tags = ["blue", "small"]
			},
			new Product {
				// The colon in the name is load-bearing: it proves the filter parser stops at the operator
				// token and takes the rest of the criterion verbatim.
				Id = 6, Name = "Doohickey: legacy", Description = "old stock", Status = ProductStatus.Discontinued,
				Rank = 60, Price = 99.00m, CreatedAt = Epoch.AddDays(6), Tags = [],
				DiscontinuedAt = Epoch.AddDays(200),
				CategoryId = 1, Category = electronics
			},
			new Product {
				Id = 7, Name = "APPLE", Description = "uppercase", Status = ProductStatus.Active,
				Rank = 70, Price = 3.00m, CreatedAt = Epoch.AddDays(7), Tags = ["green"],
				CategoryId = 3, Category = food
			},
			new Product {
				Id = 8, Name = "apple pie", Description = "lowercase", Status = ProductStatus.Active,
				Rank = 80, Price = 4.00m, CreatedAt = Epoch.AddDays(8), Tags = ["green"],
				CategoryId = 3, Category = food
			}
		];

		foreach (var product in products) {
			product.ExternalId = ExternalId(product.Id);
			product.IsFeatured = product.Id is 1 or 7;
		}

		return products;

	}

	/// <summary>
	///     The configuration every test uses unless it needs a narrower one. Operator sets are deliberately
	///     uneven — <c>rank</c> has no pattern operators, which is what makes "operator not allowed here" testable.
	/// </summary>
	public readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(defaultLimit: 3, maxLimit: 50)
		.Sortable("id", p => p.Id)
		.Sortable("name", p => p.Name)
		.Sortable("rank", p => p.Rank)
		.Sortable("status", p => p.Status)
		.Sortable("createdAt", p => p.CreatedAt)
		.Sortable("reviewCount", p => p.Reviews.Count)
		.DefaultSortBy("rank")
		.WithTieBreaker(p => p.Id)
		.Searchable("name", p => p.Name)
		.Searchable("description", p => p.Description)
		.Filterable("id", p => p.Id,
			PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.Between,
			PaginateFilterOperator.GreaterThan, PaginateFilterOperator.GreaterThanOrEqual,
			PaginateFilterOperator.LessThan, PaginateFilterOperator.LessThanOrEqual)
		.Filterable("name", p => p.Name,
			PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.Null,
			PaginateFilterOperator.StartsWith, PaginateFilterOperator.ILike, PaginateFilterOperator.Contains)
		.Filterable("description", p => p.Description,
			PaginateFilterOperator.Eq, PaginateFilterOperator.Null, PaginateFilterOperator.ILike)
		.Filterable("status", p => p.Status, PaginateFilterOperator.Eq, PaginateFilterOperator.In)
		// Null is allowed here on purpose: rank is a non-nullable value type, which is the case where
		// $null can never match and $not:$null always does.
		.Filterable("rank", p => p.Rank,
			PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.Between,
			PaginateFilterOperator.GreaterThan, PaginateFilterOperator.GreaterThanOrEqual,
			PaginateFilterOperator.LessThan, PaginateFilterOperator.LessThanOrEqual,
			PaginateFilterOperator.Null)
		.Filterable("price", p => p.Price, PaginateFilterOperator.Eq)
		.Filterable("isFeatured", p => p.IsFeatured, PaginateFilterOperator.Eq)
		.Filterable("externalId", p => p.ExternalId, PaginateFilterOperator.Eq, PaginateFilterOperator.In)
		.Filterable("createdAt", p => p.CreatedAt,
			PaginateFilterOperator.Eq, PaginateFilterOperator.Between,
			PaginateFilterOperator.GreaterThan, PaginateFilterOperator.LessThan)
		.Filterable("discontinuedAt", p => p.DiscontinuedAt,
			PaginateFilterOperator.Null, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan)
		.Filterable("categoryName", p => p.Category!.Name,
			PaginateFilterOperator.Eq, PaginateFilterOperator.ILike)
		.Filterable("tags", p => p.Tags, PaginateFilterOperator.Contains)
		.FilterableMany("reviewer", p => p.Reviews, r => r.Reviewer,
			PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.ILike)
		.FilterableMany("rating", p => p.Reviews, r => r.Rating,
			PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThanOrEqual));

}
