# Query-string contract

The complete wire format. Six inputs, nothing else:

| Parameter        | Repeatable | Example                       | Notes |
|------------------|:----------:|-------------------------------|-------|
| `page`           | no         | `?page=2`                     | 1-based, defaults to `1`. |
| `limit`          | no         | `?limit=50`                   | Defaults to the config's `DefaultLimit`, capped by `MaxLimit`. |
| `sortBy`         | **yes**    | `?sortBy=price:DESC&sortBy=name:ASC` | Applied in the order given. |
| `search`         | no         | `?search=acme`                | Free text over the searchable fields. |
| `searchBy`       | **yes**    | `?searchBy=name&searchBy=sku` | Narrows `search` to a subset of them. |
| `filter.<field>` | **yes**    | `?filter.status=$eq:Active`   | One or more criteria per field. |

**Anything else is ignored.** `offset`, `utm_source`, your own tracking parameters — the binder reads exactly
the six above and pages normally. This is deliberate: strict binding would reject perfectly ordinary client
parameters. `page` and `limit` themselves *are* validated and return `400`.

For repeatable parameters, repeat the key (`?sortBy=a:ASC&sortBy=b:DESC`). Comma-joining them in one value
does **not** work — that reads as a single malformed instruction.

Rejections are quoted inline below where they help explain a rule. The complete list, and which message wins
when a request is wrong in several ways at once, is in [Errors](../errors/); the ceilings that
produce several of them are in [Configuration API → Guards](../configuration/#guards).

## The object it binds to

The six parameters bind to `PaginateQuery`, and off the web you construct it directly — the property values
carry the **same strings** the query string does, so everything on this page still describes what they mean:

| Parameter | Property | Type | Absent means |
|-----------|----------|------|--------------|
| `page` | `Page` | `int` | `PaginateQuery.DefaultPage`, which is `1` |
| `limit` | `Limit` | `int?` | `null` → the config's `DefaultLimit` |
| `sortBy` | `SortBy` | `IReadOnlyList<string>` | empty → the config's `DefaultSortBy` |
| `search` | `Search` | `string?` | `null` → no search |
| `searchBy` | `SearchBy` | `IReadOnlyList<string>` | empty → all searchable fields |
| `filter.<field>` | `Filters` | `IReadOnlyDictionary<string, IReadOnlyList<string>>` | empty → no filters |

```csharp
var request = new PaginateQuery {
    Page   = 2,
    SortBy = ["price:DESC", "name:ASC"],           // one entry per sort, in priority order
    Filters = new Dictionary<string, IReadOnlyList<string>> {
        ["status"] = ["$eq:Active"],               // the $op: prefix stays
    },
};
```

It is a `class`, not a `record`, so there is no `with` — value equality over those collection properties
would compare by reference and lie. Use [`WithPage(n)`](../response/#paging-without-links-withpage) to derive
one request from another. Validation happens when the query executes, never here, so an out-of-range value
produces the same `400` whichever way the request was built.

For the pipeline these six parameters feed — bind, validate, filter, count, sort, page — see
[Getting started](/guide/getting-started/#what-the-engine-does-with-that-request). This page is the parameters
themselves.

::: info About the SQL on this page
Every statement quoted here was captured from the engine running against **SQLite**, so the text is that
provider's. What is stable across providers is the *shape*: which predicate an operator produces, how criteria
combine, and where the parameters sit. Collection operators in particular look different elsewhere — SQLite
reaches into a JSON column with `json_each`, another provider will not.
:::

## `page`

1-based. Must parse as a positive integer with no sign, decimal point or padding — `0`, `-1`, `+2`, `2.0` and
`abc` all return `400 Query parameter 'page' must be a positive integer.`

The value becomes the `OFFSET`, always as a parameter:

```sql
-- ?page=2&limit=3
SELECT "p"."Id", "p"."Name", "p"."Status", "p"."Rank"
FROM "Products" AS "p"
ORDER BY "p"."Rank", "p"."Id"
LIMIT @p OFFSET @p
```

Pages past the end are **not** an error: you get `items: []` with truthful `meta`, and the engine skips the
second query entirely.

## `limit`

Omitted → the config's `DefaultLimit`. Supplied → must be between `1` and the config's `MaxLimit`, otherwise
`400 Query parameter 'limit' must be between 1 and 100.` An over-large limit is **rejected, not clamped** —
silently returning fewer rows than asked for is the harder bug to notice.

## `sortBy`

```
sortBy=<field>:<ASC|DESC>
```

Both parts are required — a bare `?sortBy=name` is a `400`. The direction is case-insensitive; the field name
is matched against the configured `Sortable` names, also case-insensitively. Repeat the parameter for
secondary sorts, in priority order:

```http
GET /products?sortBy=status:ASC&sortBy=rank:DESC
```

```sql
ORDER BY "p"."Status", "p"."Rank" DESC, "p"."Id"
```

That trailing `"p"."Id"` is the configured
[tie-breaker](../configuration/#withtiebreaker), appended to every query so that rows which compare
equal cannot swap places between pages.

- More than [`MaxSortFields`](../configuration/#guards) (default 5) → `400`.
- A field that is not configured sortable (or is disabled for this caller via [`.When(...)`](../configuration/#when)) → `400 Sort for field 'x' is not configured.`
- No `sortBy` at all → the config's `DefaultSortBy` entries, in declaration order.
- The configured **tie-breaker is always appended last**, whichever of the two applied.

If there is no `sortBy`, no `DefaultSortBy` and no tie-breaker, the engine refuses the request rather than
paging an unordered set:

> Pagination requires a deterministic sort order. Pass 'sortBy', configure DefaultSortBy(...), or add
> WithTieBreaker(...) to the pagination configuration.

## `search` and `searchBy`

`search` matches a substring, case-insensitively on PostgreSQL with the `.PostgreSql` package (see
[Providers](/integrations/postgresql/)), otherwise per the column collation. The term is matched against every
configured searchable field and the results OR'd together:

```http
GET /products?search=gizmo
```

```sql
WHERE "p"."Name" LIKE @p ESCAPE '\'
   OR ("p"."Description" IS NOT NULL AND "p"."Description" LIKE @p ESCAPE '\')
```

Two details worth reading off that: the same parameter is reused for every field, and a **nullable** column
gets an explicit `IS NOT NULL` companion so the predicate stays three-valued-logic safe.

`searchBy` narrows the same term to a subset:

```http
GET /products?search=gizmo&searchBy=name
```

```sql
WHERE "p"."Name" LIKE @p ESCAPE '\'
```

- `%`, `_` and `[` in the term are escaped — hence the `ESCAPE '\'` — so they match literally rather than as
  wildcards. `[` only opens a character range on SQL Server, but escaping it everywhere keeps one pattern
  correct on every provider: PostgreSQL and SQLite read any escaped character as a literal.
- Longer than `MaxSearchLength` (default 256) → `400`.
- A `searchBy` field that is not searchable → `400`; the same field twice → `400`. Both are validated **even
  when `search` is absent**, so a typo surfaces instead of silently searching everything.
- If the config declares no searchable fields at all, sending `search` is a `400`, not a no-op.
- `IgnoreSearchByInQueryParam()` in the config makes `searchBy` ignored entirely; search then always spans all
  searchable fields.

**Search and filters are AND'ed**, with the search block parenthesised as a unit:

```http
GET /products?filter.status=$eq:Active&search=widget
```

```sql
WHERE "p"."Status" = @p
  AND ("p"."Name" LIKE @p1 ESCAPE '\' OR ("p"."Description" IS NOT NULL AND "p"."Description" LIKE @p1 ESCAPE '\'))
```

## `filter.<field>`

### Grammar

```
filter.<field> = [$not:] [$and: | $or:] $<operator>[:<value>[,<value>…]]
```

The prefixes are optional and order-independent. Parsing splits on the first `:` at each step and stops at the
first operator token — **everything after that colon is the value, verbatim**. That rule is what lets values
carry colons of their own:

```http
?filter.createdAt=$gte:2026-01-01T00:00:00Z
?filter.name=$eq:Doohickey: legacy
```

The second one filters for the literal name `Doohickey: legacy`. Nothing after the operator's colon is
inspected again.

Field names are matched case-insensitively, and case variants of the same field collapse into one entry, so
`?filter.Status=…&filter.status=…` is two criteria on one field rather than two fields.

### Operator reference

Each field whitelists its own operators in the config; sending one that is not whitelisted for that field is a
`400`, even though it exists. The SQL column shows the predicate the engine builds, with the surrounding
`SELECT`/`ORDER BY` trimmed away.

Tokens are what a caller sends; the config grants them by their `PaginateFilterOperator` member name, and the
two are spelled differently often enough to be worth a table:

| Token | Member | Token | Member |
|-------|--------|-------|--------|
| `$eq` | `Eq` | `$lt` | `LessThan` |
| `$in` | `In` | `$lte` | `LessThanOrEqual` |
| `$null` | `Null` | `$gt` | `GreaterThan` |
| `$sw` | `StartsWith` | `$gte` | `GreaterThanOrEqual` |
| `$ilike` | `ILike` | `$btw` | `Between` |
| `$contains` | `Contains` | | |

Tokens are matched case-insensitively. `$not`, `$and` and `$or` are **modifiers, not operators** — they have
no enum member, are always available, and cannot be granted or withheld.

#### `$eq` — equality

```http
?filter.status=$eq:Active
```
```sql
WHERE "p"."Status" = @p
```

#### `$in` — one of

Comma-separated. On SQLite the list arrives as a single JSON parameter rather than an inlined `IN (…)`, which
is exactly the point of parameterising: one cached plan, whatever the list contains.

```http
?filter.status=$in:Active,Draft
```
```sql
WHERE "p"."Status" IN (SELECT "p0"."value" FROM json_each(@p) AS "p0")
```

An empty list is `400 Filter 'x' requires at least one '$in' value.`

#### `$null` — is null

Takes no value, and refuses one — `?filter.description=$null:false` is a
`400 Filter 'description' does not take a value for '$null'.` rather than a filter that reads as one thing and
does the other. `$not:$null` is how you ask for the opposite:

```http
?filter.description=$null
```
```sql
WHERE "p"."Description" IS NULL
```

On a **non-nullable** column the predicate is constant-folded, and the result is visible in the SQL rather
than merely described:

```http
?filter.rank=$null          → WHERE 0        (matches nothing, and the second query never runs)
?filter.rank=$not:$null     → no WHERE       (matches everything)
```

#### `$sw` — starts with

```http
?filter.name=$sw:Wid
```
```sql
WHERE "p"."Name" LIKE @p ESCAPE '\'      -- parameter: 'Wid%'
```

#### `$ilike` and `$contains` on a string — contains

The two are the same predicate on a string field:

```http
?filter.name=$ilike:widget
?filter.name=$contains:widget
```
```sql
WHERE "p"."Name" LIKE @p ESCAPE '\'      -- parameter: '%widget%'
```

With the `.PostgreSql` package registered, the same request emits native `ILIKE` instead. Without it, `$ilike`
is only as case-insensitive as the column's collation — the token names the intent, not a guarantee.

#### `$contains` on a collection — set containment

On a collection field, `$contains` means the collection holds **all** the listed values, so each value becomes
its own `AND`-ed predicate:

```http
?filter.tags=$contains:red
```
```sql
WHERE @p IN (SELECT "t"."value" FROM json_each("p"."Tags") AS "t")
```

```http
?filter.tags=$contains:red,small
```
```sql
WHERE @p  IN (SELECT "t"."value"  FROM json_each("p"."Tags") AS "t")
  AND @p1 IN (SELECT "t0"."value" FROM json_each("p"."Tags") AS "t0")
```

An empty list is `400 Filter 'x' requires at least one '$contains' value.`, and `$contains` on a scalar
non-string field is `400 Filter 'x' supports '$contains' only for string or collection fields.`

#### `$lt` `$lte` `$gt` `$gte` — comparisons

```http
?filter.rank=$gte:3
```
```sql
WHERE "p"."Rank" >= @p
```

Numbers and dates are the obvious cases. **`string`, `Guid` and enums order too**, and the ordering is the
database's, not .NET's:

| Field type | Ordered by | Worth knowing |
|------------|-----------|---------------|
| `string` | the column's **collation** | `$gt:m` returns different rows under a case-sensitive and a case-insensitive collation. The engine does not impose one. |
| `Guid` | the database's byte order | which is not always .NET's `Guid.CompareTo` order. The same divergence already applies to sorting a `Guid` column; filters inherit it rather than introduce it. |
| enums | the **underlying integral value**, not the member name | so it follows declaration order. A model that maps the enum to text cannot translate this. |
| `bool` | — | `400 Filter 'x' does not support comparison operators for type 'Boolean'.` There is no ordering to ask for; use `$eq`. |

A `NULL` column never matches a comparison, in either direction — the same three-valued logic `$eq` follows.

#### `$btw` — inclusive range

Exactly two comma-separated values; anything else is `400 Filter 'x' requires exactly two '$btw' values.`
It expands to the pair of comparisons rather than a `BETWEEN` keyword:

```http
?filter.rank=$btw:20,50
```
```sql
WHERE "p"."Rank" >= @p AND "p"."Rank" <= @p1
```

Which is byte-for-byte what `?filter.rank=$gte:20&filter.rank=$lte:50` produces. `$btw` is a shorthand, not a
different query.

### Fields that are not columns

A filterable field can point at a related entity's property, in which case the engine joins:

```http
?filter.categoryName=$eq:Electronics
```
```sql
FROM "Products" AS "p"
LEFT JOIN "Categories" AS "c" ON "p"."CategoryId" = "c"."Id"
WHERE "c"."Name" = @p
```

A field declared with `FilterableMany(...)` matches when **any** element of a sub-collection satisfies the
criterion, which becomes an `EXISTS`:

```http
?filter.reviewer=$eq:ann
```
```sql
WHERE EXISTS (SELECT 1 FROM "Reviews" AS "r" WHERE "p"."Id" = "r"."ProductId" AND "r"."Reviewer" = @p)
```

```http
?filter.rating=$gte:4
```
```sql
WHERE EXISTS (SELECT 1 FROM "Reviews" AS "r" WHERE "p"."Id" = "r"."ProductId" AND "r"."Rating" >= @p)
```

Note what that means for a request combining two of them: `?filter.reviewer=$eq:ann&filter.rating=$gte:4`
matches a product with a review by `ann` **and** a review rated 4 or better — not necessarily the same review.
Each criterion gets its own `EXISTS`. When you need "the same element satisfies both", model it as one
filterable field over a computed value.

### `$not` — negation

Negates the single criterion it prefixes, and the negation reaches the SQL rather than wrapping it in a `NOT (…)`:

```http
?filter.status=$not:$eq:Discontinued     → WHERE "p"."Status" <> @p
?filter.name=$not:$ilike:apple           → WHERE "p"."Name" NOT LIKE @p ESCAPE '\'
?filter.deletedAt=$not:$null             → WHERE "p"."DeletedAt" IS NOT NULL
```

### `$and` / `$or` — combining criteria on one field

Repeat `filter.<field>` to apply several criteria to the same field. Each criterion says how it joins the ones
before it; the default is `$and`:

```http
# 20 <= rank <= 50
?filter.rank=$gte:20&filter.rank=$lte:50
```
```sql
WHERE "p"."Rank" >= @p AND "p"."Rank" <= @p1
```

```http
# status is Active OR Draft
?filter.status=$eq:Active&filter.status=$or:$eq:Draft
```
```sql
WHERE "p"."Status" = @p OR "p"."Status" = @p1
```

Criteria on **different** fields are always joined with `AND`. There is no cross-field `OR` and no grouping /
parentheses — that is a deliberate ceiling on how much query language is exposed. Combine on one field, and
express anything richer as a dedicated filterable field.

```mermaid
flowchart LR
    A["filter.rank=$gte:20"] --> AND1(("AND"))
    B["filter.rank=$lte:50"] --> AND1
    AND1 --> AND2(("AND"))
    C["filter.status=$eq:Active"] --> OR1(("OR"))
    D["filter.status=$or:$eq:Draft"] --> OR1
    OR1 --> AND2
    AND2 --> W["WHERE"]
```

### Value formats

| Target type | Accepted |
|-------------|----------|
| `string` | anything, used verbatim |
| `bool` | `true` / `false`, case-insensitive (not `1` / `0`) |
| `Guid` | any format `Guid.TryParse` accepts |
| integers, `decimal`, `float`, `double` | invariant culture — `.` as the decimal separator |
| `DateTime`, `DateTimeOffset` | invariant culture; a value with no offset is read as **UTC** |
| enums | **by name only**, case-insensitive (`Active`, `active`). Numeric values are rejected, and so is a number that does not correspond to a defined member. |
| `Instant`, `LocalDate` | ISO-8601, with the `.NodaTime` package |
| anything else | `400`, unless registered via [`PaginateTypeSupport`](/integrations/custom-types/) |

An empty value (`?filter.price=$eq:`) is `null` for a nullable target and a `400` for a non-nullable one. Use
`$null` rather than relying on that.

**No escaping inside value lists.** `$in`, `$btw` and `$contains`-on-a-collection split on `,` and trim; a
value that itself contains a comma cannot be expressed. Single-value operators take the value whole, commas
included.

Values are emitted as SQL **parameters** (`EF.Parameter`), never inlined literals — every `@p` on this page is
that at work. The database can reuse one plan across every value your callers send, and a value can never be
read as SQL.

## One request, end to end

```http
GET /products?page=1&limit=3&sortBy=status:ASC&sortBy=rank:DESC
              &search=wid&filter.status=$eq:Active&filter.rank=$btw:20,50
```

becomes one count and one page, both fully parameterised:

```sql
SELECT COUNT(*)
FROM "Products" AS "p"
WHERE "p"."Status" = @p AND "p"."Rank" >= @p1 AND "p"."Rank" <= @p2 AND ("p"."Name" LIKE @p3 ESCAPE '\' OR ("p"."Description" IS NOT NULL AND "p"."Description" LIKE @p3 ESCAPE '\'))

SELECT "p"."Id", "p"."Name", "p"."Status", "p"."Rank"
FROM "Products" AS "p"
WHERE "p"."Status" = @p AND "p"."Rank" >= @p1 AND "p"."Rank" <= @p2 AND ("p"."Name" LIKE @p3 ESCAPE '\' OR ("p"."Description" IS NOT NULL AND "p"."Description" LIKE @p3 ESCAPE '\'))
ORDER BY "p"."Status", "p"."Rank" DESC, "p"."Id"
LIMIT @p8 OFFSET @p7
```

Four criteria, one `WHERE`, and not a single value inlined. Note the parameter numbering: the engine reuses
`@p3` for both halves of the search, and the paging parameters land at the end wherever the count of earlier
parameters puts them.

The `SELECT` list is the projection's, not the entity's — see [Projections](/guide/projections/) for how that
list is decided and how to widen it without loading the whole row.
