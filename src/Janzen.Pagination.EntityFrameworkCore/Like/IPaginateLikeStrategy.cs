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
	/// <remarks>
	///     The escaping keys off <see cref="PaginateLikeDefaults.EscapeCharacter" />, so the call this returns
	///     <b>must</b> declare that character as its explicit <c>ESCAPE</c> argument. Omit it and the engine's
	///     escaping of <c>\</c>, <c>%</c>, <c>_</c> and <c>[</c> stays in the pattern as literal text the data
	///     would have to contain, so every value carrying one of those characters silently stops matching. The
	///     match set only ever narrows, and it narrows wherever the provider has no default escape character —
	///     SQLite and SQL Server among them; PostgreSQL and MySQL treat <c>\</c> as the default and keep working
	///     by accident. <see cref="PaginateLikeStrategyBase" /> is that shell already written; derive from it
	///     rather than re-deriving it.
	/// </remarks>
	Expression BuildLike(Expression value, Expression pattern);

}
