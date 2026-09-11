using Janzen.Pagination.EntityFrameworkCore.Like;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

// Threaded through expression building: which leg is being composed for, which LIKE strategy, and the two
// search-length guards -- the pattern operators emit the same LIKE '%...%' the search term does, so the numbers
// that bound one have to reach the other, and the field builder has no access to the configuration.
//
// TWO flags, not one negated. UseDatabaseFunctions is "this is EF Core, emit EF.Parameter and EF.Functions";
// InMemory is "this is EnumerableQuery, emit the constructs LINQ-to-Objects needs and a database would refuse".
// They are not complements: a third-party provider that is neither — a synchronous LINQ provider over a
// database — answers false to both, and gets the plain translatable tree rather than either specialisation.
// Reading `!UseDatabaseFunctions` as "in memory" is what handed such a provider string.Compare with a
// StringComparison and a null-safe rewrite it has no use for, on a path that previously worked.
internal readonly record struct PaginateExpressionContext(
	bool UseDatabaseFunctions,
	bool InMemory,
	IPaginateLikeStrategy LikeStrategy,
	int MinSearchLength,
	int MaxSearchLength
);
