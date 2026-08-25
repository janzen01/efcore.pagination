# Testing your pagination

A `PaginateConfig` is a published contract, so it is worth a test — and it does not need a database. This page
is what to assert, where to assert it, and the two SQLite behaviours that decide which tests can live where.

## Test the config without a database

Against a plain `IQueryable`, the engine takes a different path: `EF.Functions.Like` becomes
`string.IndexOf(..., OrdinalIgnoreCase)` and the async terminal operators become their synchronous
equivalents. Filters, search, sort, paging and projection all still run, so a list is enough:

```csharp
var products = new List<Product> {
    new() { Id = Guid.NewGuid(), Name = "Widget",     Status = ProductStatus.Active,        Price = 10m },
    new() { Id = Guid.NewGuid(), Name = "Wid-gadget", Status = ProductStatus.Active,        Price = 30m },
    new() { Id = Guid.NewGuid(), Name = "Gizmo",      Status = ProductStatus.Discontinued,  Price = 20m },
}.AsQueryable();

var request = new PaginateQuery {
    Limit = 10,
    Search = "wid",
    Filters = new Dictionary<string, IReadOnlyList<string>> { ["status"] = ["$eq:Active"] },
};

var page = await products.PaginateAsync<Product, ProductDto>(request, config);

Assert.Equal(2, page.Meta.TotalItems);                                  // search is case-insensitive
Assert.Equivalent(["Widget", "Wid-gadget"], page.Items.Select(p => p.Name));
```

This is a test of your **configuration** — that the right fields are exposed with the right operators, and
that the wrong request is refused. It is not a test of the generated SQL: LINQ-to-Objects and a real provider
do not agree on collation or null ordering, and pretending otherwise produces a test that passes locally and
lies about production.

So **assert the set, not the order**, unless the test data pins the sort keys unambiguously. LINQ-to-Objects
orders strings with the current culture's comparer, which is not what the database will do.

## Test the refusals too

Half the value of an allow-list is what it rejects, and rejections are the cheapest thing here to test:

```csharp
var ex = await Assert.ThrowsAsync<PaginateQueryException>(() =>
    products.PaginateAsync<Product, ProductDto>(
        new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> {
            ["price"] = ["$ilike:10"] } }, config));

Assert.Contains("does not support operator", ex.Message);
```

Match on a **fragment** rather than the whole message. The wording is part of the published contract, but a
test that pins it whole turns any future clarification into a failing test for no gain. Every message is in
[Errors](/reference/errors/).

Worth covering, because each is a real way to break an API without noticing:

- a field you did **not** declare is refused (the allow-list holds);
- an operator you did not grant for a field is refused **for that field** even though it exists;
- a `.When(false)` field is refused with the same message as an unknown one, so the gate does not leak;
- `MaxLimit` is refused rather than clamped.

## Test the config builds

Three checks cannot run until the whole config is known, so they fire at the end of `Create` — see
[Configuration API](/reference/configuration/#create-and-what-it-defers). A config that compiles can still
throw on first use, which in a web app means the first request after a deploy.

One test that simply calls `Create` moves that failure to CI:

```csharp
[Fact]
public void Config_builds() => Assert.NotNull(ProductPaginateConfigProvider.Config);
```

A static config field is initialised lazily, so touching it is what runs the validation.

## Assert the SQL, without running it

`ApplyPagination` composes the page query and stops there, so `ToQueryString()` prints the statement the
engine would execute — no server, no log scraping, no round-trip:

```csharp
[Fact]
public void An_active_filter_reaches_the_indexed_column() {

    var request = new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> {
        ["status"] = ["$eq:Active"]
    } };

    string sql = _db.Products.ApplyPagination(request, ProductConfig.Instance).Query.ToQueryString();

    Assert.Contains("\"Status\" = ", sql);

}
```

This is the test to reach for when the question is "does my `Filterable` reach the column I indexed" rather
than "does it return the right rows". It needs a real provider — that is what generates SQL — but not a
reachable server: a `DbContext` built on a connection string nobody opens is enough.

The same handle answers the other half of the doubt: `ApplyPaginateFilters(...).Query` is the match set, so
`CountAsync` on it tells you what the filter selected without paging getting in the way. See
[Query composers](/reference/composers/).

## When you do need a database

Two things a plain `IQueryable` cannot tell you: whether an expression **translates**, and what the query
**returns**. SQLite in-memory covers both without Docker, and is what this library's own suite uses.

Two SQLite limits shape what may be asserted there. **Neither is caused by the engine** — both reproduce with
a plain `Where` and no pagination involved — but both will surprise you:

- **`DateTimeOffset` comparisons do not translate at all.** Any test of a date filter has to run on the plain
  `IQueryable` path instead, or against a real provider.
- **Decimals are stored as TEXT**, and the collation parses them with the **current culture**. Ordering by a
  decimal column therefore *throws outright* on a machine whose decimal separator is not a dot. Order and
  range over an integer instead, and keep decimal coverage to equality.

The second one is a genuine trap for a mixed-locale team: the same test suite passes on one developer's
machine and fails on another's, for a reason that has nothing to do with the code under test.

## Watch the process-wide statics

Two pieces of state are global and outlive a test:

- `PaginateLikeDefaults.Strategy` — what `UsePostgreSql()` sets. A test that swaps it changes behaviour for
  every test running concurrently, so keep those in a non-parallel collection and restore the previous value
  afterwards.
- `PaginateTypeSupport` registrations **cannot be undone**, and the three methods do not behave alike on a
  repeat call: a value parser or simple type registered twice for the same type **replaces** the earlier
  one, while a projection conversion is **appended** — registering the same delegate twice installs it
  twice. Only `PaginateNodaTime.Register()` is genuinely idempotent, guarded by a flag. Register all of
  them once, in a fixture, and never per test.

## What this library does not test

Native PostgreSQL `ILIKE` and its `ESCAPE` behaviour need a real PostgreSQL server, so they are not covered
by the in-process suite here. If you rely on `UsePostgreSql()`, that is the seam worth one integration test of
your own — see [PostgreSQL](/integrations/postgresql/).
