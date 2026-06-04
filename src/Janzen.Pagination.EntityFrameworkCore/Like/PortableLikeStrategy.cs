using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Like;

// Portable fallback: EF.Functions.Like translates to SQL LIKE on every provider.
// Case sensitivity follows the column collation; install Janzen.Pagination.PostgreSql for true ILIKE.
internal sealed class PortableLikeStrategy : IPaginateLikeStrategy {

	private const string EscapeCharacter = "\\";

	private readonly static MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
		nameof(DbFunctionsExtensions.Like),
		[typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

	public PaginateFilterOperator? PreferredExampleOperator => null;

	public Expression BuildLike(Expression value, Expression pattern) {
		var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
		return Expression.Call(LikeMethod, functions, value, pattern, Expression.Constant(EscapeCharacter));
	}

}
