using Janzen.Pagination.EntityFrameworkCore.Like;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

// Threaded through expression building: whether to emit DB functions (EF.Parameter, LIKE/ILIKE), which strategy,
// and the two search-length guards -- the pattern operators emit the same LIKE '%...%' the search term does, so
// the numbers that bound one have to reach the other, and the field builder has no access to the configuration.
internal readonly record struct PaginateExpressionContext(
	bool UseDatabaseFunctions,
	IPaginateLikeStrategy LikeStrategy,
	int MinSearchLength,
	int MaxSearchLength
);
