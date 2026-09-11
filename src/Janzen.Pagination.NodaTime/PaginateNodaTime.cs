using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Model;

using NodaTime;
using NodaTime.Text;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Xml;

namespace Janzen.Pagination.NodaTime;

/// <summary>
///     Registers NodaTime support with the Janzen.Pagination engine: value parsing for filters, leaf-type
///     classification for projection, and projection conversions onto the BCL types a DTO holds. Call once at
///     startup, before the first configuration is built — e.g. <c>services.AddPagination(p =&gt; p.UseNodaTime())</c>
///     — or call <see cref="Register" /> directly for non-DI hosts.
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
	// Volatile because the fast path below reads it outside the lock: it is the publication point for every
	// registration written before it, and a plain bool guards no earlier write.
	private static volatile bool _registered;

	// One row per conversion, so the nullable composition below is written once. Every pair goes NodaTime -> BCL:
	// entities hold NodaTime, DTOs consume BCL types, and nothing has asked for the reverse.
	private readonly static (Type Source, Type Target, string Method)[] Conversions = [
		(typeof(Instant), typeof(DateTimeOffset), nameof(Instant.ToDateTimeOffset)),
		(typeof(LocalDate), typeof(DateOnly), nameof(LocalDate.ToDateOnly)),
		(typeof(LocalDateTime), typeof(DateTime), nameof(LocalDateTime.ToDateTimeUnspecified)),
		(typeof(LocalTime), typeof(TimeOnly), nameof(LocalTime.ToTimeOnly)),
		(typeof(OffsetDateTime), typeof(DateTimeOffset), nameof(OffsetDateTime.ToDateTimeOffset)),
		(typeof(Duration), typeof(TimeSpan), nameof(Duration.ToTimeSpan))
	];

	/// <summary>
	///     Registers NodaTime support with the pagination engine, for hosts without dependency injection. Idempotent
	///     and process-wide: the first call registers, later ones are no-ops. Call once at startup, before the first
	///     <c>PaginateConfig&lt;TEntity&gt;</c> is built — the operator-less <c>Filterable</c> shorthand derives its
	///     operator set while the builder runs, so a later registration is too late; in a DI host,
	///     <c>UseNodaTime()</c> inside <c>AddPagination(...)</c> calls this for you.
	/// </summary>
	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
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

		throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not a valid instant.") { Code = PaginateQueryError.ValueInvalid };

	}

	/// <summary>
	///     Reads a duration in either NodaTime's own round-trip form (<c>2:30:00</c>) or ISO-8601 (<c>PT2H30M</c>),
	///     mirroring how the engine reads a <see cref="TimeSpan" />. NodaTime ships no ISO-8601 duration pattern —
	///     <c>DurationPattern.JsonRoundtrip</c> is the colon form despite the name — so the ISO leg is the engine's
	///     own reader, which goes through <see cref="XmlConvert" />. Note the two spellings do not share a
	///     resolution: the colon form is native and resolves to a nanosecond, the ISO form to 100 ns.
	/// </summary>
	private static object ParseDuration(string value) {

		// Trimmed for both spellings, the way the engine's TimeSpan branch does it. Reading the raw value here
		// made padding decide the answer — " PT2H " was a 400 on a Duration field while the same value on a
		// TimeSpan field paged — which is nothing the contract mentions.
		string duration = value.Trim();

		var roundtrip = DurationPattern.JsonRoundtrip.Parse(duration);
		if (roundtrip.Success) return roundtrip.Value;

		// The ISO leg is the engine's own: years and months are calendar-dependent and XmlConvert answers them
		// with fixed approximations, so the same refusal applies to a Duration as to a TimeSpan. One
		// implementation, and the clause below keeps this package's own 400 wording.
		try {
			return Duration.FromTimeSpan(PaginateValueConverter.ParseIsoDuration(duration, "is not a valid duration"));
		} catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) {
			throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not a valid duration.", ex) { Code = PaginateQueryError.ValueInvalid };
		}

	}

	private static object ParseNodaTime<T>(string value, IPattern<T> pattern, string displayName) {
		var result = pattern.Parse(value);
		// Echo, like every other message the engine builds from caller text: this one is interpolated straight
		// into a ProblemDetails `detail`, so an unbounded value or an embedded CR/LF reaches a log sink verbatim.
		return result.Success ? result.Value! : throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not a valid {displayName}.") { Code = PaginateQueryError.ValueInvalid };
	}

	/// <summary>
	///     Builds a NodaTime → BCL projection for one of the supported pairs, preserving nullability where both sides
	///     are nullable; returns <see langword="null" /> when no pair applies.
	/// </summary>
	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
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
