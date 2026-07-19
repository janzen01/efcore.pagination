using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Like;

// Shared shell for strategies that call an EF.Functions pattern-match method with an explicit escape character;
// derived classes supply the method (LIKE, ILIKE, ...) and the preferred example operator.
internal abstract class PaginateLikeStrategyBase(MethodInfo likeMethod) : IPaginateLikeStrategy {

	private const string EscapeCharacter = "\\";

	public abstract PaginateFilterOperator? PreferredExampleOperator { get; }

	public Expression BuildLike(Expression value, Expression pattern) {
		var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
		return Expression.Call(likeMethod, functions, value, pattern, Expression.Constant(EscapeCharacter));
	}

}
