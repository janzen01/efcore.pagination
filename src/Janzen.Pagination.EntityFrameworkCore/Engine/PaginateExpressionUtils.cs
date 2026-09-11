using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateExpressionUtils {

	private readonly static MethodInfo IndexOfMethod = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string), typeof(StringComparison)])!;

	private readonly static MethodInfo StartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string), typeof(StringComparison)])!;

	private readonly static MethodInfo ParameterMethod = typeof(EF).GetMethod(nameof(EF.Parameter))!;

	// Every pattern operator and every searched field parameterises a string by construction, and closing a
	// generic method is the expensive half of this call -- so that one instantiation is resolved once here rather
	// than per criterion.
	private readonly static MethodInfo StringParameterMethod = ParameterMethod.MakeGenericMethod(typeof(string));

	private readonly static MethodInfo OrderByMethod = GetQueryableOrderMethod(nameof(Queryable.OrderBy));
	private readonly static MethodInfo OrderByDescendingMethod = GetQueryableOrderMethod(nameof(Queryable.OrderByDescending));
	private readonly static MethodInfo ThenByMethod = GetQueryableOrderMethod(nameof(Queryable.ThenBy));
	private readonly static MethodInfo ThenByDescendingMethod = GetQueryableOrderMethod(nameof(Queryable.ThenByDescending));

	/// <summary>
	///     Wraps a value expression in <see cref="EF.Parameter{T}" /> so EF Core translates it as a SQL parameter instead
	///     of an inlined literal (better plan reuse). Only valid inside EF queries.
	/// </summary>
	public static Expression ToDatabaseParameter(Expression value) {
		return Expression.Call(value.Type == typeof(string) ? StringParameterMethod : ParameterMethod.MakeGenericMethod(value.Type), value);
	}

	/// <summary>
	///     Escapes LIKE/ILIKE wildcard characters so user input is matched literally (used together with
	///     <c>ESCAPE '\'</c>). <c>[</c> is escaped unconditionally even though only SQL Server reads it as a range
	///     opener: PostgreSQL, SQLite and SQL Server — the three engines this was verified against — treat any
	///     escaped character as a literal, so one pattern serves all three.
	/// </summary>
	/// <remarks>
	///     That is a guarantee about those three, not about every provider. One pattern stays portable wherever
	///     the provider reads <c>escape + any character</c> as that character; a provider that instead requires
	///     the escape to be followed by <c>%</c>, <c>_</c> or itself rejects an escaped <c>[</c> — Oracle raises
	///     <c>ORA-01424</c> — and one that does not support an <c>ESCAPE</c> clause at all rejects every pattern
	///     the shipped strategies build. Neither is reachable in process here, so neither is covered by tests.
	/// </remarks>
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

	/// <summary>
	///     Applies one ordering key. A <c>string</c> key on the in-memory leg is ordered with
	///     <see cref="StringComparer.InvariantCulture" />, because <c>Comparer&lt;string&gt;.Default</c> reads
	///     <see cref="CultureInfo.CurrentCulture" /> — so the page order would follow the host's own culture, and
	///     an app that opts into request localization would make it follow the caller's query string, cookie or
	///     <c>Accept-Language</c> header instead. On a relational provider the comparer is the column's collation
	///     and there is nothing here to choose.
	/// </summary>
	public static IQueryable<TEntity> ApplyOrder<TEntity>(IQueryable<TEntity> query, LambdaExpression selector, bool descending, bool first, bool useDatabaseFunctions) {

		if (!useDatabaseFunctions && selector.Body.Type == typeof(string)) {

			// Rebuilt rather than cast: the null-safe rewriter returns a LambdaExpression whose delegate type is
			// inferred, and only this form is guaranteed to be the one the overload wants.
			var key = Expression.Lambda<Func<TEntity, string>>(selector.Body, selector.Parameters);
			var comparer = StringComparer.InvariantCulture;

			// first == false means an OrderBy already ran, so the cast holds.
			return (first, descending) switch {
				(true, true) => query.OrderByDescending(key, comparer),
				(true, false) => query.OrderBy(key, comparer),
				(false, true) => ((IOrderedQueryable<TEntity>)query).ThenByDescending(key, comparer),
				(false, false) => ((IOrderedQueryable<TEntity>)query).ThenBy(key, comparer)
			};

		}

		var openMethod = (first, descending) switch {
			(true, true) => OrderByDescendingMethod,
			(true, false) => OrderByMethod,
			(false, true) => ThenByDescendingMethod,
			(false, false) => ThenByMethod
		};

		var method = openMethod.MakeGenericMethod(typeof(TEntity), selector.Body.Type);

		// Queryable.OrderBy's own body, reached without invoking it reflectively. MethodInfo.Invoke boxes its
		// arguments into an array and wraps whatever the provider throws in a TargetInvocationException, so a
		// caller's catch clause for the real exception never fired and the top log line named nothing. The node
		// built here is the one the typed overload builds, which is what keeps the composed SQL unchanged.
		return query.Provider.CreateQuery<TEntity>(Expression.Call(method, query.Expression, Expression.Quote(selector)));

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
			throw new PaginateQueryException($"Query parameter 'limit' must be between 1 and {config.MaxLimit}.") { Code = PaginateQueryError.LimitOutOfRange };
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
			if (page != PaginateQuery.DefaultPage) throw new PaginateQueryException("Query parameter 'page' must be 1 when 'limit' is -1.") { Code = PaginateQueryError.UnlimitedReadRequiresFirstPage };

			return;
		}

		if (config.MaxOffset is not { } maxOffset) return;

		// Long arithmetic so a very large page cannot overflow into a value that passes.
		long skip = (long)(page - 1) * limit;

		if (skip > maxOffset) {
			throw new PaginateQueryException($"Query parameter 'page' exceeds the allowed offset for this resource: at most {maxOffset} rows may be skipped.") { Code = PaginateQueryError.MaxOffsetExceeded };
		}

	}

	public static string FormatDirection(PaginateSortDirection direction) { return direction == PaginateSortDirection.Desc ? "DESC" : "ASC"; }

	public static PaginateSort ParseSort(string value) {

		string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
		if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) throw new PaginateQueryException($"Sort value '{value}' must use the format 'field:ASC' or 'field:DESC'.") { Code = PaginateQueryError.SortValueMalformed };

		var direction = parts[1].ToUpperInvariant() switch {
			"ASC" => PaginateSortDirection.Asc,
			"DESC" => PaginateSortDirection.Desc,
			_ => throw new PaginateQueryException($"Sort direction '{parts[1]}' is not supported.") { Code = PaginateQueryError.SortDirectionUnknown }
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
