using Janzen.Pagination.EntityFrameworkCore.Like;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

// Threaded through expression building: whether to emit DB functions (EF.Parameter, LIKE/ILIKE) and which strategy.
internal readonly record struct PaginateExpressionContext(bool UseDatabaseFunctions, IPaginateLikeStrategy LikeStrategy);
