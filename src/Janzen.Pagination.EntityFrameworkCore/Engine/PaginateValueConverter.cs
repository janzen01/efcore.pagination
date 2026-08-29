using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Xml;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateValueConverter {

	// One entry per type ever asked for, including the misses (a null delegate), so an unsupported type costs the
	// interface walk once rather than on every filter value.
	private readonly static ConcurrentDictionary<Type, Func<string, object?>?> ParsableParsers = new();

	private readonly static string[] DateOnlyFormats = ["yyyy-MM-dd"];

	private readonly static string[] TimeOnlyFormats = ["HH:mm:ss.FFFFFFF", "HH:mm:ss", "HH:mm"];

	private readonly static MethodInfo ParsableTemplate =
		typeof(PaginateValueConverter).GetMethod(nameof(ParseParsable), BindingFlags.NonPublic | BindingFlags.Static)!;

	public static object? Convert(string value, Type targetType) {

		var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (type == typeof(string)) return value;

		if (string.IsNullOrWhiteSpace(value)) {
			return Nullable.GetUnderlyingType(targetType) is not null ? null : throw new PaginateQueryException($"Value for type '{type.Name}' must not be empty.");
		}

		// The registry runs FIRST so a consumer can override a built-in decision. Consulted last — as it was until
		// 10.0.3 — a registration for an already-supported type was a silent no-op, so everyone it affected was
		// someone who tried to override and never found out they hadn't.
		if (PaginateTypeSupport.TryParseValue(type, value, out var custom)) return custom;

		if (type == typeof(Guid)) return Parse<Guid>(value, Guid.TryParse, "GUID");
		if (type == typeof(bool)) return Parse<bool>(value, bool.TryParse, "boolean");

		try {

			if (type == typeof(byte)) return byte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(sbyte)) return sbyte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(short)) return short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(ushort)) return ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(uint)) return uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(long)) return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(ulong)) return ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(float)) return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (type == typeof(double)) return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (type == typeof(decimal)) return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
			// AssumeUniversal alone reads an offsetless value as UTC and then hands back Kind=Local, which shifts the
			// comparison by the server's zone against a UTC-kind column. AdjustToUniversal is what makes the
			// documented "no offset means UTC" true on a machine that is not on UTC.
			if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
			if (type == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
			// Exact ISO forms rather than DateOnly.Parse/TimeOnly.Parse, which are lossy in opposite directions:
			// the BCL reads "2026-01-03T10:00:00" as a DateOnly and throws the time away, and reads the same string
			// as a TimeOnly and throws the date away. Answering a question the caller did not ask is the trap the
			// Instant parser refuses a bare date for; these carry no zone, so there is nothing else to interpret.
			if (type == typeof(DateOnly)) {
				return DateOnly.TryParseExact(value, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
					? date
					: throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");
			}

			if (type == typeof(TimeOnly)) {
				return TimeOnly.TryParseExact(value, TimeOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
					? time
					: throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");
			}
			// Two accepted spellings: .NET's own "c" (2:30:00) because that is what a .NET caller types, and ISO-8601
			// (PT2H30M) because it survives a URL without percent-encoded colons.
			// The colon is required for the first: TimeSpan.TryParse reads a bare "2" as two *days*, and nobody who
			// types 2 into a duration filter means that. Without a colon the value can only be ISO, where "2" is
			// malformed and answers 400 like any other bad value.
			if (type == typeof(TimeSpan)) {
				return value.Contains(':', StringComparison.Ordinal) && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan)
					? timeSpan
					: ParseIsoDuration(value, type.Name);
			}
			if (type == typeof(char)) return value.Length == 1 ? value[0] : throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");

			if (type.IsEnum) {
				// Enums are addressed by name only — numeric forms are rejected so the filter contract is stable
				// and well-defined (Enum.Parse otherwise accepts arbitrary numbers, including undefined [Flags] combinations).
				if (char.IsAsciiDigit(value[0]) || value[0] is '-' or '+') {
					throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");
				}

				object parsed = Enum.Parse(type, value, true);
				return Enum.IsDefined(type, parsed) ? parsed : throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");
			}

		} catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) {
			throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.", ex);
		}

		// Last: anything that can parse itself invariantly. This is what makes a consumer's strongly-typed id work as
		// a filter value with no registration at all — whitelisting a field of type T is the opt-in, so there is
		// deliberately no separate knob to turn it off.
		if (TryParseParsable(type, value, out var parsable)) return parsable;

		throw new PaginateQueryException($"Filtering values of type '{type.Name}' is not supported.");

	}

	private static bool TryParseParsable(Type type, string value, out object? result) {

		var parser = ParsableParsers.GetOrAdd(type, BuildParsableParser);

		if (parser is null) {
			result = null;
			return false;
		}

		result = parser(value);
		return true;

	}

	private static Func<string, object?>? BuildParsableParser(Type type) {

		// IParsable<TSelf> only — a type parsing into something other than itself is not what this fallback is for.
		bool parsable = Array.Exists(
			type.GetInterfaces(),
			i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>) && i.GenericTypeArguments[0] == type
		);

		if (!parsable) return null;

		// Through the constrained generic rather than a reflected TryParse: an explicit interface implementation has
		// no public static TryParse to find, and this dispatches to it correctly either way.
		return ParsableTemplate.MakeGenericMethod(type).CreateDelegate<Func<string, object?>>();

	}

	private static object? ParseParsable<T>(string value) where T : IParsable<T> {
		// A TryParse that answers true with a null result would otherwise turn a filter into an IS NULL against a
		// target the caller declared non-nullable. Only reachable for a class-based T; a struct boxes.
		return T.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed is not null
			? parsed
			: throw new PaginateQueryException($"Value '{value}' is not valid for type '{typeof(T).Name}'.");
	}

	/// <summary>
	///     Reads an ISO-8601 duration, refusing the calendar-dependent designators. <c>XmlConvert</c> answers
	///     <c>P1M</c> with exactly thirty days and <c>P1Y</c> with 365 — a fixed approximation of something that has
	///     no fixed length — so a filter for "a month" would silently be a filter for thirty days.
	/// </summary>
	internal static TimeSpan ParseIsoDuration(string value, string typeName) {

		int time = value.IndexOf('T', StringComparison.Ordinal);
		var datePart = time < 0 ? value.AsSpan() : value.AsSpan(0, time);

		if (datePart.ContainsAny('Y', 'M')) {
			throw new PaginateQueryException($"Value '{value}' is not valid for type '{typeName}': a duration in years or months has no fixed length.");
		}

		return XmlConvert.ToTimeSpan(value);

	}

	private static T Parse<T>(string value, TryParse<T> parser, string displayName) {
		return parser(value, out var parsed)
			? parsed
			: throw new PaginateQueryException($"Value '{value}' is not a valid {displayName}.");
	}

	private delegate bool TryParse<T>(string value, out T result);

}
