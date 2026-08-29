using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Model;

using NodaTime;
using NodaTime.Text;

using System.Linq.Expressions;
using System.Xml;

namespace Janzen.Pagination.NodaTime;

/// <summary>
///     Registers NodaTime support with the Janzen.Pagination engine: value parsing for filters, leaf-type
///     classification for projection, and projection conversions onto the BCL types a DTO holds. Call once at
///     startup before serving requests — e.g. <c>services.AddPagination(p =&gt; p.UseNodaTime())</c> — or call
///     <see cref="Register" /> directly for non-DI hosts.
/// </summary>
/// <remarks>
///     Supported: <see cref="Instant" />, <see cref="LocalDate" />, <see cref="LocalDateTime" />,
///     <see cref="LocalTime" />, <see cref="OffsetDateTime" />, <see cref="Duration" /> and
///     <see cref="YearMonth" />, all read as ISO-8601 in the invariant culture. Deliberately absent:
///     <c>ZonedDateTime</c> (no canonical text form without deciding on a zone provider — store an
///     <see cref="Instant" /> and present it zoned), <c>Period</c> (calendar arithmetic, not a comparable filter
///     value) and <c>Interval</c> (two-valued; a <c>$btw</c> over <see cref="Instant" /> covers it).
/// </remarks>
public static class PaginateNodaTime {

	private readonly static Lock Gate = new();
	private static bool _registered;

	// One row per conversion, so the nullable composition below is written once. Every pair goes NodaTime -> BCL:
	// entities hold NodaTime, DTOs consume BCL types, and nothing has asked for the reverse.
	private readonly static (Type Source, Type Target, string Method)[] Conversions = [
		(typeof(Instant), typeof(DateTimeOffset), nameof(Instant.ToDateTimeOffset)),
		(typeof(LocalDate), typeof(DateOnly), nameof(LocalDate.ToDateOnly)),
		(typeof(LocalDateTime), typeof(DateTime), nameof(LocalDateTime.ToDateTimeUnspecified)),
		(typeof(LocalTime), typeof(TimeOnly), nameof(LocalTime.ToTimeOnly)),
		(typeof(OffsetDateTime), typeof(DateTimeOffset), nameof(OffsetDateTime.ToDateTimeOffset))
	];

	/// <summary>
	///     Registers NodaTime support with the pagination engine, for hosts without dependency injection. Idempotent
	///     and process-wide: the first call registers, later ones are no-ops. Call once at startup, before the first
	///     query runs; in a DI host, <c>UseNodaTime()</c> inside <c>AddPagination(...)</c> calls this for you.
	/// </summary>
	public static void Register() {

		if (_registered) return;

		lock (Gate) {

			if (_registered) return;

			PaginateTypeSupport.RegisterValueParser(typeof(Instant), ParseInstant);
			PaginateTypeSupport.RegisterValueParser(typeof(LocalDate), value => ParseNodaTime(value, LocalDatePattern.Iso, "local date"));
			PaginateTypeSupport.RegisterValueParser(typeof(LocalDateTime), value => ParseNodaTime(value, LocalDateTimePattern.ExtendedIso, "local date-time"));
			PaginateTypeSupport.RegisterValueParser(typeof(LocalTime), value => ParseNodaTime(value, LocalTimePattern.ExtendedIso, "local time"));
			PaginateTypeSupport.RegisterValueParser(typeof(OffsetDateTime), value => ParseNodaTime(value, OffsetDateTimePattern.ExtendedIso, "offset date-time"));
			PaginateTypeSupport.RegisterValueParser(typeof(YearMonth), value => ParseNodaTime(value, YearMonthPattern.Iso, "year-month"));
			PaginateTypeSupport.RegisterValueParser(typeof(Duration), ParseDuration);

			PaginateTypeSupport.RegisterSimpleType(typeof(Instant));
			PaginateTypeSupport.RegisterSimpleType(typeof(LocalDate));
			PaginateTypeSupport.RegisterSimpleType(typeof(LocalDateTime));
			PaginateTypeSupport.RegisterSimpleType(typeof(LocalTime));
			PaginateTypeSupport.RegisterSimpleType(typeof(OffsetDateTime));
			PaginateTypeSupport.RegisterSimpleType(typeof(Duration));
			PaginateTypeSupport.RegisterSimpleType(typeof(YearMonth));

			PaginateTypeSupport.RegisterProjectionConversion(BuildConversion);

			_registered = true;

		}

	}

	/// <summary>
	///     Reads an instant, accepting both <c>2026-08-29T10:30:00Z</c> and an offset form such as
	///     <c>2026-08-29T10:30:00+02:00</c> — the latter names exactly one instant, and rejecting it was a gap
	///     rather than a contract. A bare date is still refused: it would silently mean midnight.
	/// </summary>
	private static object ParseInstant(string value) {

		var utc = InstantPattern.ExtendedIso.Parse(value);
		if (utc.Success) return utc.Value;

		var offset = OffsetDateTimePattern.ExtendedIso.Parse(value);
		if (offset.Success) return offset.Value.ToInstant();

		throw new PaginateQueryException($"Value '{value}' is not a valid instant.");

	}

	/// <summary>
	///     Reads a duration in either NodaTime's own round-trip form (<c>2:30:00</c>) or ISO-8601 (<c>PT2H30M</c>),
	///     mirroring how the engine reads a <see cref="TimeSpan" />. NodaTime ships no ISO-8601 duration pattern —
	///     <c>DurationPattern.JsonRoundtrip</c> is the colon form despite the name — so the ISO leg goes through
	///     <see cref="XmlConvert" />.
	/// </summary>
	private static object ParseDuration(string value) {

		var roundtrip = DurationPattern.JsonRoundtrip.Parse(value);
		if (roundtrip.Success) return roundtrip.Value;

		// Years and months are calendar-dependent and XmlConvert answers them with fixed approximations — P1M is
		// exactly thirty days, P1Y exactly 365. A filter for "a month" silently becoming a filter for thirty days
		// is worse than a 400, so those designators are refused rather than approximated.
		int time = value.IndexOf('T', StringComparison.Ordinal);
		var datePart = time < 0 ? value.AsSpan() : value.AsSpan(0, time);

		if (datePart.ContainsAny('Y', 'M')) {
			throw new PaginateQueryException($"Value '{value}' is not a valid duration: a duration in years or months has no fixed length.");
		}

		try {
			return Duration.FromTimeSpan(XmlConvert.ToTimeSpan(value));
		} catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) {
			throw new PaginateQueryException($"Value '{value}' is not a valid duration.");
		}

	}

	private static object ParseNodaTime<T>(string value, IPattern<T> pattern, string displayName) {
		var result = pattern.Parse(value);
		return result.Success ? result.Value! : throw new PaginateQueryException($"Value '{value}' is not a valid {displayName}.");
	}

	/// <summary>
	///     Builds a NodaTime → BCL projection for one of the supported pairs, preserving nullability where both sides
	///     are nullable; returns <see langword="null" /> when no pair applies.
	/// </summary>
	private static Expression? BuildConversion(Expression sourceValue, Type targetType) {

		var sourceUnderlying = Nullable.GetUnderlyingType(sourceValue.Type) ?? sourceValue.Type;
		var targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

		string? method = null;
		foreach (var (source, target, name) in Conversions) {
			if (sourceUnderlying == source && targetUnderlying == target) {
				method = name;
				break;
			}
		}

		if (method is null) return null;

		bool sourceNullable = Nullable.GetUnderlyingType(sourceValue.Type) is not null;
		bool targetNullable = Nullable.GetUnderlyingType(targetType) is not null;

		if (!sourceNullable) {
			var converted = Expression.Call(sourceValue, method, Type.EmptyTypes);
			return targetNullable ? Expression.Convert(converted, targetType) : converted;
		}

		// A nullable source can only be projected onto a nullable target; otherwise let the engine raise a clear error.
		if (!targetNullable) return null;

		var value = Expression.Call(Expression.Property(sourceValue, "Value"), method, Type.EmptyTypes);
		return Expression.Condition(
			Expression.Property(sourceValue, "HasValue"),
			Expression.Convert(value, targetType),
			Expression.Constant(null, targetType)
		);

	}

}
