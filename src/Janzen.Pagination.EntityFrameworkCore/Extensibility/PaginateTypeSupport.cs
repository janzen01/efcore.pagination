using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore;

/// <summary>
///     Append-only registry that lets add-on packages teach the engine about additional value types — e.g. the
///     <c>Janzen.Pagination.NodaTime</c> package registers <c>Instant</c>/<c>LocalDate</c> support here. Type support
///     is universal (not per-request), so registrations are process-wide and meant to be made once at startup
///     (typically via an add-on extension such as <c>UseNodaTime()</c>) before the first query runs. Registration is
///     additive and idempotent-friendly; nothing here is mutated per request.
/// </summary>
public static class PaginateTypeSupport {

	private readonly static ConcurrentDictionary<Type, Func<string, object?>> ValueParsers = new();
	private readonly static ConcurrentDictionary<Type, byte> SimpleTypes = new();
	private readonly static Lock Gate = new();
	private static Func<Expression, Type, Expression?>[] _projectionConversions = [];

	/// <summary>Registers a parser converting a raw string filter value into <paramref name="type" />.</summary>
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
	///     and returns the converted expression, or <see langword="null" /> when it does not apply.
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
