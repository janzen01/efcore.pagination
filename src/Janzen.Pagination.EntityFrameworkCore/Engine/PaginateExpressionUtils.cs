using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateExpressionUtils {

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
	///     <c>ESCAPE '\'</c>). <c>[</c> is escaped unconditionally even though only SQL Server reads it as a range
	///     opener: PostgreSQL and SQLite treat any escaped character as a literal, so one pattern stays portable.
	/// </summary>
	public static string EscapeLikePattern(string value) {
		return value
			.Replace("\\", @"\\", StringComparison.Ordinal)
			.Replace("%", "\\%", StringComparison.Ordinal)
			.Replace("_", "\\_", StringComparison.Ordinal)
			.Replace("[", "\\[", StringComparison.Ordinal);
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

	/// <summary>
	///     The effective page size, or <see cref="PaginateQuery.UnlimitedLimit" /> when the caller asked for every
	///     row and the configuration allows it. Everything downstream treats that value as "no Skip, no Take".
	/// </summary>
	public static int ParseLimit(PaginateQuery request, IPaginateConfig config) {

		if (!request.Limit.HasValue) return config.DefaultLimit;

		int limit = request.Limit.Value;

		// Only the exact literal, and only where the resource opted in. -2 and 0 stay a 400 either way, so the
		// message names the ordinary range rather than advertising a mode this resource may not have.
		if (limit == PaginateQuery.UnlimitedLimit && config.UnlimitedMaxRows is not null) return limit;

		if (limit < 1 || limit > config.MaxLimit) {
			throw new PaginateQueryException($"Query parameter 'limit' must be between 1 and {config.MaxLimit}.");
		}

		return limit;

	}

	/// <summary>
	///     Rejects a page whose offset exceeds the configured ceiling, and an unlimited request for any page but the
	///     first. Pure arithmetic, so it runs before anything is counted or fetched.
	/// </summary>
	public static void ValidateOffset(int page, int limit, IPaginateConfig config) {

		if (limit == PaginateQuery.UnlimitedLimit) {
			// Pages of an unbounded set are meaningless: there is exactly one.
			if (page != PaginateQuery.DefaultPage) throw new PaginateQueryException("Query parameter 'page' must be 1 when 'limit' is -1.");

			return;
		}

		if (config.MaxOffset is not { } maxOffset) return;

		// Long arithmetic so a very large page cannot overflow into a value that passes.
		long skip = (long)(page - 1) * limit;

		if (skip > maxOffset) {
			throw new PaginateQueryException($"Query parameter 'page' exceeds the allowed offset for this resource: at most {maxOffset} rows may be skipped.");
		}

	}

	public static string FormatDirection(PaginateSortDirection direction) { return direction == PaginateSortDirection.Desc ? "DESC" : "ASC"; }

	public static PaginateSort ParseSort(string value) {

		string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
		if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) throw new PaginateQueryException($"Sort value '{value}' must use the format 'field:ASC' or 'field:DESC'.");

		var direction = parts[1].ToUpperInvariant() switch {
			"ASC" => PaginateSortDirection.Asc,
			"DESC" => PaginateSortDirection.Desc,
			_ => throw new PaginateQueryException($"Sort direction '{parts[1]}' is not supported.")
		};

		return new PaginateSort(parts[0], direction);

	}

	/// <summary>Resolves a method overload by name and parameter count; <c>Single</c> guards against ambiguous matches.</summary>
	public static MethodInfo GetMethodByParameterCount(Type type, string name, int parameterCount) {
		return type
			.GetMethods()
			.Single(method => method.Name == name && method.GetParameters().Length == parameterCount);
	}

	private static MethodInfo GetQueryableOrderMethod(string name) { return GetMethodByParameterCount(typeof(Queryable), name, 2); }

}
