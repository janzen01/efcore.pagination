using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Like;

// Portable fallback: EF.Functions.Like translates to SQL LIKE on every provider.
// Case sensitivity follows the column collation; install Janzen.Pagination.PostgreSql for true ILIKE.
internal sealed class PortableLikeStrategy() : PaginateLikeStrategyBase(LikeMethod) {

	private readonly static MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
		nameof(DbFunctionsExtensions.Like),
		[typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

	public override PaginateFilterOperator? PreferredExampleOperator => null;

}
