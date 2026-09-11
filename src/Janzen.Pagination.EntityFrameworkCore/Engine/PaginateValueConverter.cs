using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Xml;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateValueConverter {

	// One entry per type ever asked for, including the misses (a null delegate), so an unsupported type costs the
	// interface walk once rather than on every filter value.
	private readonly static ConcurrentDictionary<Type, Func<string, string, object?>?> ParsableParsers = new();

	private readonly static string[] DateOnlyFormats = ["yyyy-MM-dd"];

	private readonly static string[] TimeOnlyFormats = ["HH:mm:ss.FFFFFFF", "HH:mm:ss", "HH:mm"];

	// A date is mandatory and the offset optional — "K" matches nothing, "Z" or "+HH:mm". Parse completes a
	// date-less value from the current clock, so "10:00" meant 10:00 *today* and a stored filter link changed
	// meaning at midnight. Same reasoning as DateOnlyFormats, on the two types most requests actually use.
	private readonly static string[] TimestampFormats = [
		"yyyy-MM-dd",
		"yyyy-MM-ddTHH:mmK",
		"yyyy-MM-ddTHH:mm:ssK",
		"yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
	];

	// TimeSpan.TryParse re-reads the colon form as d.hh:mm:ss the moment the first component passes 23, so
	// "24:00:00" selected everything within twenty-four *days* while "25:30:00" was a 400. These cap the hour
	// instead; a day count keeps its own unambiguous spelling in the ISO leg (P5D).
	private readonly static string[] DurationFormats = [@"h\:m", @"h\:m\:s", @"h\:m\:s\.FFFFFFF"];

	// NumberStyles.Number additionally allows a group separator and a *trailing* sign, which no other numeric
	// type here accepts: "1,5" parsed as fifteen on a money field and "1234-" as minus 1234. One grammar for the
	// whole family, matching the invariant dot separator the reference promises.
	private const NumberStyles DecimalStyles =
		NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

	// The exact forms above carry no whitespace of their own, so padding is requested here rather than
	// inherited from the pattern. It is not universal: the numeric branches get it from NumberStyles.Integer
	// and NumberStyles.Float, but DateOnly, TimeOnly and the colon TimeSpan form below parse exact with no
	// whitespace flag at all, and char compares Length == 1.
	private const DateTimeStyles TimestampStyles =
		DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite;

	private readonly static MethodInfo ParsableTemplate =
		typeof(PaginateValueConverter).GetMethod(nameof(ParseParsable), BindingFlags.NonPublic | BindingFlags.Static)!;

	/// <summary>
	///     Converts one raw query-string value to <paramref name="targetType" />. Every failure names
	///     <paramref name="field" /> rather than the CLR type behind it: a type name in a 400 tracks the domain
	///     model closely enough that probing a few fields reconstructs it, and the field name is the token the
	///     caller actually sent.
	/// </summary>
	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	public static object? Convert(string value, Type targetType, string field) {

		var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (type == typeof(string)) return value;

		if (string.IsNullOrWhiteSpace(value)) {
			return Nullable.GetUnderlyingType(targetType) is not null ? null : throw new PaginateQueryException($"Value for '{field}' must not be empty.") { Code = PaginateQueryError.ValueEmpty };
		}

		// Everything below is reachable by consumer code — a registered parser, or an IParsable<TSelf>.TryParse the
		// engine found on its own — so the translation guard starts above the registry rather than five lines
		// below it. Outside it, a parser throwing the way the guide teaches was an unhandled 500 from a query
		// string, and registering an override for a built-in type silently downgraded that field from 400 to 500.
		try {

			// The registry runs FIRST so a consumer can override a built-in decision. Consulted last — as it was
			// until 10.0.3 — a registration for an already-supported type was a silent no-op, so everyone it
			// affected was someone who tried to override and never found out they hadn't.
			if (PaginateTypeSupport.TryParseValue(type, value, out var custom)) {
				// Func<string, object?> invites it, but a parser signals bad input by throwing and null is not an
				// answer: against a target that cannot hold one it reached Expression.Constant(null, typeof(T)).
				return custom is null && targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
					? throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid }
					: custom;
			}

			if (type == typeof(Guid)) return Parse<Guid>(value, Guid.TryParse, "GUID");
			if (type == typeof(bool)) return Parse<bool>(value, bool.TryParse, "boolean");

			if (type == typeof(byte)) return byte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(sbyte)) return sbyte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(short)) return short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(ushort)) return ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(uint)) return uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(long)) return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(ulong)) return ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			// The IEEE types saturate where the integer types throw, and NumberStyles.Float reads "NaN" and
			// "Infinity" by name, so an out-of-range magnitude answered an empty page indistinguishable from
			// "no rows match". A value the type cannot hold is the same 400 the integer family already gives.
			if (type == typeof(float)) {
				float single = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
				return float.IsFinite(single) ? single : throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}

			if (type == typeof(double)) {
				double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
				return double.IsFinite(number) ? number : throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}
			if (type == typeof(decimal)) return decimal.Parse(value, DecimalStyles, CultureInfo.InvariantCulture);
			// AssumeUniversal alone reads an offsetless value as UTC and then hands back Kind=Local, which shifts the
			// comparison by the server's zone against a UTC-kind column. AdjustToUniversal is what makes the
			// documented "no offset means UTC" true on a machine that is not on UTC.
			if (type == typeof(DateTimeOffset)) {
				return DateTimeOffset.TryParseExact(value, TimestampFormats, CultureInfo.InvariantCulture, TimestampStyles, out var moment)
					? moment
					: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}

			if (type == typeof(DateTime)) {
				return DateTime.TryParseExact(value, TimestampFormats, CultureInfo.InvariantCulture, TimestampStyles, out var instant)
					? instant
					: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}
			// Exact ISO forms rather than DateOnly.Parse/TimeOnly.Parse, which are lossy in opposite directions:
			// the BCL reads "2026-01-03T10:00:00" as a DateOnly and throws the time away, and reads the same string
			// as a TimeOnly and throws the date away. Answering a question the caller did not ask is the trap the
			// Instant parser refuses a bare date for; these carry no zone, so there is nothing else to interpret.
			if (type == typeof(DateOnly)) {
				return DateOnly.TryParseExact(value, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
					? date
					: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}

			if (type == typeof(TimeOnly)) {
				return TimeOnly.TryParseExact(value, TimeOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
					? time
					: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}
			// Two accepted spellings: .NET's own "c" (2:30:00) because that is what a .NET caller types, and ISO-8601
			// (PT2H30M) because it survives a URL without percent-encoded colons.
			// The colon is required for the first: TimeSpan.TryParse reads a bare "2" as two *days*, and nobody who
			// types 2 into a duration filter means that. Without a colon the value can only be ISO, where "2" is
			// malformed and answers 400 like any other bad value.
			if (type == typeof(TimeSpan)) {

				string duration = value.Trim();

				if (!duration.Contains(':', StringComparison.Ordinal)) return ParseIsoDuration(value, $"is not valid for '{field}'");

				// A custom TimeSpan pattern cannot carry a sign, so the minus comes off first and TimeSpanStyles
				// puts it back — otherwise pinning the hour would also drop every negative duration.
				bool negative = duration.StartsWith('-');

				return TimeSpan.TryParseExact(negative ? duration[1..] : duration, DurationFormats, CultureInfo.InvariantCulture,
					negative ? TimeSpanStyles.AssumeNegative : TimeSpanStyles.None, out var timeSpan)
					? timeSpan
					: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };

			}
			if (type == typeof(char)) return value.Length == 1 ? value[0] : throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };

			if (type.IsEnum) {
				// Enums are addressed by one declared member name only — numeric forms are rejected so the filter
				// contract is stable and well-defined (Enum.Parse otherwise accepts arbitrary numbers, including
				// undefined [Flags] combinations). Both guards read the *trimmed* candidate, because Enum.Parse
				// trims before it looks at anything: reading value[0] let " 1" walk past, and a bare '+' decodes
				// to a space on the wire. A comma list goes with them — Enum.Parse OR-combines it arithmetically,
				// so "Draft,Active" resolved to Active and silently dropped every Draft row. $in is the operator
				// that takes several values.
				string member = value.Trim();

				if (char.IsAsciiDigit(member[0]) || member[0] is '-' or '+' || member.Contains(',', StringComparison.Ordinal)) {
					throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
				}

				object parsed = Enum.Parse(type, member, true);
				return Enum.IsDefined(type, parsed) ? parsed : throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
			}

			// Last: anything that can parse itself invariantly. This is what makes a consumer's strongly-typed id work
			// as a filter value with no registration at all — whitelisting a field of type T is the opt-in, so there
			// is deliberately no separate knob to turn it off. TryParse carries no obligation not to throw, so it
			// belongs under the same guard as the registry.
			if (TryParseParsable(type, value, field, out var parsable)) return parsable;

		} catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) {
			throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.", ex) { Code = PaginateQueryError.ValueInvalid };
		}

		throw new PaginateQueryException($"Filtering values for '{field}' is not supported.") { Code = PaginateQueryError.ValueInvalid };

	}

	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	private static bool TryParseParsable(Type type, string value, string field, out object? result) {

		var parser = ParsableParsers.GetOrAdd(type, BuildParsableParser);

		if (parser is null) {
			result = null;
			return false;
		}

		result = parser(value, field);
		return true;

	}

	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	private static Func<string, string, object?>? BuildParsableParser(Type type) {

		// IParsable<TSelf> only — a type parsing into something other than itself is not what this fallback is for.
		bool parsable = Array.Exists(
			type.GetInterfaces(),
			i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>) && i.GenericTypeArguments[0] == type
		);

		if (!parsable) return null;

		// Through the constrained generic rather than a reflected TryParse: an explicit interface implementation has
		// no public static TryParse to find, and this dispatches to it correctly either way.
		return ParsableTemplate.MakeGenericMethod(type).CreateDelegate<Func<string, string, object?>>();

	}

	private static object? ParseParsable<T>(string value, string field) where T : IParsable<T> {
		// A TryParse that answers true with a null result would otherwise turn a filter into an IS NULL against a
		// target the caller declared non-nullable. Only reachable for a class-based T; a struct boxes.
		return T.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed is not null
			? parsed
			: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not valid for '{field}'.") { Code = PaginateQueryError.ValueInvalid };
	}

	/// <summary>
	///     Reads an ISO-8601 duration, refusing the calendar-dependent designators. <c>XmlConvert</c> answers
	///     <c>P1M</c> with exactly thirty days and <c>P1Y</c> with 365 — a fixed approximation of something that has
	///     no fixed length — so a filter for "a month" would silently be a filter for thirty days. Shared with the
	///     NodaTime package's <c>Duration</c> leg, which is why the rejection's middle clause arrives as
	///     <paramref name="invalidClause" />: one rule, and each leg keeps the <c>400</c> wording it already ships.
	/// </summary>
	internal static TimeSpan ParseIsoDuration(string value, string invalidClause) {

		int time = value.IndexOf('T', StringComparison.Ordinal);
		var datePart = time < 0 ? value.AsSpan() : value.AsSpan(0, time);

		if (datePart.ContainsAny('Y', 'M')) {
			throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' {invalidClause}: a duration in years or months has no fixed length.") { Code = PaginateQueryError.ValueInvalid };
		}

		return XmlConvert.ToTimeSpan(value);

	}

	private static T Parse<T>(string value, TryParse<T> parser, string displayName) {
		return parser(value, out var parsed)
			? parsed
			: throw new PaginateQueryException($"Value '{PaginateInputGuard.Echo(value)}' is not a valid {displayName}.") { Code = PaginateQueryError.ValueInvalid };
	}

	private delegate bool TryParse<T>(string value, out T result);

}
