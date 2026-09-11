using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore;

/// <summary>
///     Append-only registry that lets add-on packages teach the engine about additional value types — e.g. the
///     <c>Janzen.Pagination.NodaTime</c> package registers <c>Instant</c>/<c>LocalDate</c> support here. Type support
///     is universal (not per-request), so registrations are process-wide and are meant to be made once at startup,
///     typically via an add-on extension such as <c>UseNodaTime()</c>. Nothing here is mutated per request.
/// </summary>
/// <remarks>
///     <b>The deadline is the first config, not the first query.</b> The operator-less <c>Filterable(name, expr)</c>
///     shorthand derives its operator set while the builder runs, so a registration that lands after a config was
///     constructed is already too late for that config — it must come before the first
///     <c>PaginateConfig&lt;TEntity&gt;.Create(…)</c> naming one of its types, not merely before the first request.
///     A late registration is worse than it looks for projections: the builder caches per
///     <c>(TEntity, TResult)</c> pair, and a <b>failed</b> build is cached too, so one request served before the
///     registration lands keeps that pair failing for the lifetime of the process even after the registration
///     arrives. The cure is registration order, not a retry.
///     Registration is additive and cannot be undone. <c>RegisterValueParser</c> and <c>RegisterSimpleType</c>
///     assign by type, so calling either twice is harmless; <c>RegisterProjectionConversion</c> appends and does
///     <b>not</b> deduplicate — see its own summary. Both dictionaries are keyed by <see cref="Type" /> and live as
///     long as the process, so registering a type from a collectible <c>AssemblyLoadContext</c> keeps that context
///     loaded: register from the host, not from a plugin that expects to be unloaded.
/// </remarks>
public static class PaginateTypeSupport {

	private readonly static ConcurrentDictionary<Type, Func<string, object?>> ValueParsers = new();
	private readonly static ConcurrentDictionary<Type, byte> SimpleTypes = new();
	private readonly static Lock Gate = new();
	private static Func<Expression, Type, Expression?>[] _projectionConversions = [];

	/// <summary>
	///     Registers a parser converting a raw string filter value into <paramref name="type" />. Signal bad input by
	///     throwing <see cref="Model.PaginateQueryException" /> — that is what answers <c>400</c> rather than
	///     <c>500</c>. A <see cref="FormatException" />, <see cref="ArgumentException" /> or
	///     <see cref="OverflowException" /> answers the same <c>400</c> under a generic message; every other
	///     exception type is still a <c>500</c>. Return a value rather than <see langword="null" />: against a field
	///     whose type cannot hold one it is that same <c>400</c>, but on a nullable or reference-typed field it reads
	///     as absence and the filter becomes <c>IS NULL</c>, which is what <c>$null</c> exists for.
	/// </summary>
	public static void RegisterValueParser(Type type, Func<string, object?> parser) {
		ArgumentNullException.ThrowIfNull(type);
		ArgumentNullException.ThrowIfNull(parser);
		ValueParsers[type] = parser;
	}

	/// <summary>Marks <paramref name="type" /> as a leaf type so the projection builder does not recurse into it.</summary>
	public static void RegisterSimpleType(Type type) {
		ArgumentNullException.ThrowIfNull(type);
		SimpleTypes[type] = 0;
	}

	/// <summary>
	///     Registers a projection conversion. The delegate receives the source member expression and the target type
	///     and returns the converted expression, or <see langword="null" /> when it does not apply. Unlike the two
	///     registries above this one compares nothing and appends unconditionally, so a second call leaves two
	///     entries that every projection argument then walks, and each retained delegate roots whatever its target
	///     captures. Call it once per <b>process</b>, not once per host built in one — guard it with a flag of your
	///     own, the way <c>UseNodaTime()</c> does.
	/// </summary>
	public static void RegisterProjectionConversion(Func<Expression, Type, Expression?> tryConvert) {
		ArgumentNullException.ThrowIfNull(tryConvert);
		lock (Gate) {
			_projectionConversions = [.. _projectionConversions, tryConvert];
		}
	}

	internal static bool TryParseValue(Type type, string value, out object? result) {
		if (ValueParsers.TryGetValue(type, out var parser)) {
			result = parser(value);
			return true;
		}

		result = null;
		return false;
	}

	internal static bool IsRegisteredSimpleType(Type type) { return SimpleTypes.ContainsKey(type); }

	internal static Expression? TryBuildProjectionConversion(Expression sourceValue, Type targetType) {
		return _projectionConversions.Select(convert => convert(sourceValue, targetType)).OfType<Expression>().FirstOrDefault();
	}

}
