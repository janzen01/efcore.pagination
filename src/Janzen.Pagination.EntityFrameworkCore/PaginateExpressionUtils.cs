using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore;

internal static class PaginateExpressionUtils {

	// Escape character used together with ILIKE so user-supplied '%'/'_' are matched literally.
	private const string LikeEscapeCharacter = "\\";

	private readonly static MethodInfo IndexOfMethod = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string), typeof(StringComparison)])!;

	private readonly static MethodInfo StartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string), typeof(StringComparison)])!;

	private readonly static MethodInfo ParameterMethod = typeof(EF).GetMethod(nameof(EF.Parameter))!;

	private readonly static MethodInfo OrderByMethod = GetQueryableOrderMethod(nameof(Queryable.OrderBy));
	private readonly static MethodInfo OrderByDescendingMethod = GetQueryableOrderMethod(nameof(Queryable.OrderByDescending));
	private readonly static MethodInfo ThenByMethod = GetQueryableOrderMethod(nameof(Queryable.ThenBy));
	private readonly static MethodInfo ThenByDescendingMethod = GetQueryableOrderMethod(nameof(Queryable.ThenByDescending));

	/// <summary>
	///     Wraps a value expression in <see cref="EF.Parameter{T}" /> so EF Core translates it as a SQL parameter instead
	///     of an inlined literal (better plan reuse). Only valid inside EF queries.
	/// </summary>
	public static Expression ToDatabaseParameter(Expression value) { return Expression.Call(ParameterMethod.MakeGenericMethod(value.Type), value); }

	/// <summary>
	///     Escapes LIKE/ILIKE wildcard characters so user input is matched literally (used together with
	///     <c>ESCAPE '\'</c>).
	/// </summary>
	public static string EscapeLikePattern(string value) {
		return value
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("%", "\\%", StringComparison.Ordinal)
			.Replace("_", "\\_", StringComparison.Ordinal);
	}

	public static Expression BuildInMemoryStringMatchExpression(Expression valueExpression, string value, bool startsWith) {
		return startsWith
			? Expression.Call(valueExpression, StartsWithMethod, Expression.Constant(value), Expression.Constant(StringComparison.OrdinalIgnoreCase))
			: Expression.GreaterThanOrEqual(
				Expression.Call(valueExpression, IndexOfMethod, Expression.Constant(value), Expression.Constant(StringComparison.OrdinalIgnoreCase)),
				Expression.Constant(0, typeof(int))
			);
	}

	public static IQueryable<TEntity> ApplyOrder<TEntity>(IQueryable<TEntity> query, LambdaExpression selector, bool descending, bool first) {

		var openMethod = (first, descending) switch {
			(true, true) => OrderByDescendingMethod,
			(true, false) => OrderByMethod,
			(false, true) => ThenByDescendingMethod,
			(false, false) => ThenByMethod
		};

		var method = openMethod.MakeGenericMethod(typeof(TEntity), selector.Body.Type);

		return (IQueryable<TEntity>)method.Invoke(null, [query, selector])!;

	}

	public static int ParseLimit(PaginateQuery request, IPaginateConfig config) {

		if (!request.Limit.HasValue) return config.DefaultLimit;

		int limit = request.Limit.Value;

		if (limit < 1 || limit > config.MaxLimit) {
			throw new PaginateQueryException($"Query parameter 'limit' must be between 1 and {config.MaxLimit}.");
		}

		return limit;

	}

	public static string FormatDirection(PaginateSortDirection direction) { return direction == PaginateSortDirection.Desc ? "DESC" : "ASC"; }

	public static PaginateSort ParseSort(string value) {

		string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
		if (parts.Length != 2 || parts[0].IsNullOrWhiteSpace()) throw new PaginateQueryException($"Sort value '{value}' must use the format 'field:ASC' or 'field:DESC'.");

		var direction = parts[1].ToUpperInvariant() switch {
			"ASC" => PaginateSortDirection.Asc,
			"DESC" => PaginateSortDirection.Desc,
			_ => throw new PaginateQueryException($"Sort direction '{parts[1]}' is not supported.")
		};

		return new PaginateSort(parts[0], direction);

	}

	private static MethodInfo GetQueryableOrderMethod(string name) {
		return typeof(Queryable)
			.GetMethods()
			.Single(method => method.Name == name && method.GetParameters().Length == 2);
	}

}
