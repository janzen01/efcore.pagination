using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Like;

/// <summary>
///     The escaping shell both shipped strategies are, published so a strategy of your own does not have to
///     re-derive it: it calls <paramref name="likeMethod" /> — an <c>EF.Functions</c> pattern-match overload
///     whose parameters are <c>(DbFunctions, string matchExpression, string pattern, string escapeCharacter)</c> —
///     and passes <see cref="PaginateLikeDefaults.EscapeCharacter" /> as the last argument, which is the part
///     an implementation written from the interface alone tends to miss.
/// </summary>
/// <param name="likeMethod">The four-parameter <c>EF.Functions</c> method to call, e.g. <c>EF.Functions.Like</c>.</param>
public abstract class PaginateLikeStrategyBase(MethodInfo likeMethod) : IPaginateLikeStrategy {

	// Both nodes are process constants -- EF.Functions is a static property on a static type, and the escape
	// character is a literal -- while BuildLike runs per pattern criterion and per searched field. Expression
	// nodes are immutable, so one instance is shared by every tree built from here, exactly as the shipped
	// strategies already share their MethodInfo. A name that stopped resolving now fails at type initialization
	// rather than on a request.
	private readonly static MemberExpression Functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));

	private readonly static ConstantExpression Escape = Expression.Constant(PaginateLikeDefaults.EscapeCharacter);

	/// <inheritdoc />
	public abstract PaginateFilterOperator? PreferredExampleOperator { get; }

	/// <inheritdoc />
	public Expression BuildLike(Expression value, Expression pattern) { return Expression.Call(likeMethod, Functions, value, pattern, Escape); }

}
