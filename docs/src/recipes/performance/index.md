# Performance and indexing

Pagination turns a screen into a query shape your database will serve thousands of times, and the config is
where that shape gets decided. This page is what to index, what a page actually costs, and where the design
stops being cheap.

## What one request costs

Every page is **two queries**: a `COUNT(*)` over the filtered set, then the page fetch. They share the same
`WHERE`, so a filter that is expensive is expensive twice.

There is one saving built in: when the count comes back `0`, the second query is **never sent**. A filter that
cannot match anything therefore costs exactly one count, and a page past the end costs the same.

The count is usually the expensive half. It cannot use `LIMIT` to stop early — it has to resolve the whole
matching set, however large — while the page fetch stops after `limit` rows once the ordering is satisfied by
an index.

## Index what you exposed, not what you query

The config is a published list of query shapes. Read it as an index checklist:

- every **`Sortable`** field is an `ORDER BY` a client can ask for;
- every **`Filterable`** field is a `WHERE` clause, and every operator you granted is a different predicate
  against it;
- every **`Searchable`** field is a `LIKE '%term%'` — which no ordinary B-tree index can serve.

The ones that hurt are the ones you granted without meaning to. `$ilike` on an unindexed text column is a
sequential scan any caller can trigger, on demand, as often as they like. Granting `Eq` and `In` and stopping
there is a performance decision, not just a security one.

## Index the sort, including the tie-breaker

The emitted `ORDER BY` is not what you declared — the tie-breaker is appended to it:

```sql
ORDER BY "p"."Status", "p"."Rank" DESC, "p"."Id"
```

So a covering index has to include the tie-breaker column as its **last** key, in that order, or the database
sorts anyway:

```sql
CREATE INDEX ix_products_status_rank_id ON products (status, rank DESC, id);
```

This is worth checking against a real plan rather than assuming. An index on `(status, rank)` alone looks
right and still leaves a sort node in the plan, because two rows tied on both still need ordering by `id`.

Sorts your callers actually send are worth indexing; the full cross-product of `Sortable` fields is not. Look
at what the clients ask for before adding five indexes.

## Deep pages are the cliff

`Skip(n)` compiles to `OFFSET n`, and a database serves `OFFSET 50000` by producing fifty thousand rows and
discarding them. Cost grows with the page number, so page 1 benchmarks fine and page 500 does not.

Three responses, in increasing order of effort:

1. **Cap it.** If nobody has a real reason to reach page 500, reject deep pages at the edge. A `400` is
   cheaper than the query.
2. **Raise `limit` instead of `page`.** Walking a set in 500-row pages touches the offset problem twenty
   times less often than 25-row pages do. This is what a batch export should do — see
   [Pagination without ASP.NET Core](../without-aspnetcore/).
3. **Filter instead of paging.** A client that wants the tail usually wants a different sort, not page 500.
   `?sortBy=createdAt:DESC` beats paging to the end of `createdAt:ASC`, and costs the same as page 1.

Keyset pagination avoids the problem entirely, and this library does not implement it: the contract is
`page` and `limit`, which is what makes a total count and random page access possible in the first place.

## Keep `MaxLimit` honest

`MaxLimit` is the worst case you have agreed to serve — one request, that many rows, plus whatever the
projection pulls per row. Set it against the width of the projection, not by habit. A `MaxLimit` of 1000 over
a DTO with a sub-collection is a very different promise from 1000 over four scalar columns.

## Keep the projection narrow

The strategy you pick decides how many columns cross the wire. Only
[`PaginateMapAsync`](/guide/projections/) materialises the whole entity; the other three send a `SELECT` list
built from what the DTO actually names. On a wide table that difference dwarfs anything above.

## Measuring it

There is no handle to call `ToQueryString()` on mid-flight, because the engine composes onto your `IQueryable`
and then executes. Read what actually ran instead:

```csharp
options.UseNpgsql(connectionString).LogTo(Console.WriteLine, LogLevel.Information);
```

Two statements per request, and both are worth putting through `EXPLAIN` once with realistic data volumes.
Values arrive as parameters rather than literals, so one plan is reused across everything your callers send —
which is good for the cache and means a plan you check once stays the plan you get.
