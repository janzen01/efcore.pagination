using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Reflection;

namespace Janzen.Pagination.PostgreSql.Like;

// Emits PostgreSQL's native ILIKE for true case-insensitive search.
internal sealed class NpgsqlLikeStrategy() : PaginateLikeStrategyBase(ILikeMethod) {

	private readonly static MethodInfo ILikeMethod = PaginateExpressionUtils.GetMethodByParameterCount(
		typeof(NpgsqlDbFunctionsExtensions),
		nameof(NpgsqlDbFunctionsExtensions.ILike),
		4);

	public override PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

}
