using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Model;

using NodaTime;
using NodaTime.Text;

using System.Linq.Expressions;

namespace Janzen.Pagination.NodaTime;

/// <summary>
///     Registers NodaTime support (<see cref="Instant" /> / <see cref="LocalDate" />) with the Janzen.Pagination
///     engine: value parsing for filters, leaf-type classification for projection, and an
///     <see cref="Instant" /> → <see cref="DateTimeOffset" /> projection conversion. Call once at startup before
///     serving requests — e.g. <c>services.AddPagination(p =&gt; p.UseNodaTime())</c> — or call
///     <see cref="Register" /> directly for non-DI hosts.
/// </summary>
public static class PaginateNodaTime {

	private readonly static Lock Gate = new();
	private static bool _registered;

	public static void Register() {

		if (_registered) return;

		lock (Gate) {

			if (_registered) return;

			PaginateTypeSupport.RegisterValueParser(typeof(Instant), value => ParseNodaTime(value, InstantPattern.ExtendedIso, "instant"));
			PaginateTypeSupport.RegisterValueParser(typeof(LocalDate), value => ParseNodaTime(value, LocalDatePattern.Iso, "local date"));

			PaginateTypeSupport.RegisterSimpleType(typeof(Instant));
			PaginateTypeSupport.RegisterSimpleType(typeof(LocalDate));
			PaginateTypeSupport.RegisterSimpleType(typeof(LocalDateTime));

			PaginateTypeSupport.RegisterProjectionConversion(BuildInstantToDateTimeOffset);

			_registered = true;

		}

	}

	private static object ParseNodaTime<T>(string value, IPattern<T> pattern, string displayName) {
		var result = pattern.Parse(value);
		return result.Success ? result.Value! : throw new PaginateQueryException($"Value '{value}' is not a valid {displayName}.");
	}

	/// <summary>
	///     Builds an <see cref="Instant" /> → <see cref="DateTimeOffset" /> projection, preserving nullability where
	///     both sides are nullable; returns <see langword="null" /> when the conversion does not apply.
	/// </summary>
	private static Expression? BuildInstantToDateTimeOffset(Expression sourceValue, Type targetType) {

		var sourceUnderlying = Nullable.GetUnderlyingType(sourceValue.Type) ?? sourceValue.Type;
		var targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (sourceUnderlying != typeof(Instant) || targetUnderlying != typeof(DateTimeOffset)) return null;

		bool sourceNullable = Nullable.GetUnderlyingType(sourceValue.Type) is not null;
		bool targetNullable = Nullable.GetUnderlyingType(targetType) is not null;

		if (!sourceNullable) {
			var converted = Expression.Call(sourceValue, nameof(Instant.ToDateTimeOffset), Type.EmptyTypes);
			return targetNullable ? Expression.Convert(converted, targetType) : converted;
		}

		// A nullable Instant can only be projected onto a nullable DateTimeOffset; otherwise let the engine raise a clear error.
		if (!targetNullable) return null;

		var value = Expression.Call(Expression.Property(sourceValue, "Value"), nameof(Instant.ToDateTimeOffset), Type.EmptyTypes);
		return Expression.Condition(
			Expression.Property(sourceValue, "HasValue"),
			Expression.Convert(value, targetType),
			Expression.Constant(null, targetType)
		);

	}

}
