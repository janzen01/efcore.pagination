using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore.Like;

/// <summary>Builds a database-translatable case-insensitive pattern match.</summary>
public interface IPaginateLikeStrategy {

	/// <summary>
	///     The operator that best represents this strategy in generated documentation.
	///     Return <see langword="null" /> to fall back to the first operator configured on each field.
	/// </summary>
	PaginateFilterOperator? PreferredExampleOperator { get; }

	/// <summary>
	///     Builds the pattern-match expression. <paramref name="value" /> is the column expression;
	///     <paramref name="pattern" /> is the (already escaped and EF-parameterised) LIKE pattern.
	/// </summary>
	Expression BuildLike(Expression value, Expression pattern);

}
