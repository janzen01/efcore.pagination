using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Globalization;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateValueConverter {

	public static object? Convert(string value, Type targetType) {

		var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (type == typeof(string)) return value;

		if (string.IsNullOrWhiteSpace(value)) {
			return Nullable.GetUnderlyingType(targetType) is not null ? null : throw new PaginateQueryException($"Value for type '{type.Name}' must not be empty.");
		}

		if (type == typeof(Guid)) return Parse<Guid>(value, Guid.TryParse, "GUID");
		if (type == typeof(bool)) return Parse<bool>(value, bool.TryParse, "boolean");

		try {

			if (type == typeof(short)) return short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(long)) return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(float)) return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (type == typeof(double)) return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (type == typeof(decimal)) return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
			// AssumeUniversal alone reads an offsetless value as UTC and then hands back Kind=Local, which shifts the
			// comparison by the server's zone against a UTC-kind column. AdjustToUniversal is what makes the
			// documented "no offset means UTC" true on a machine that is not on UTC.
			if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
			if (type == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

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

		// Types contributed by add-on packages (e.g. NodaTime's Instant/LocalDate via PaginateTypeSupport).
		if (PaginateTypeSupport.TryParseValue(type, value, out var custom)) return custom;

		throw new PaginateQueryException($"Filtering values of type '{type.Name}' is not supported.");

	}

	private static T Parse<T>(string value, TryParse<T> parser, string displayName) {
		return parser(value, out var parsed)
			? parsed
			: throw new PaginateQueryException($"Value '{value}' is not a valid {displayName}.");
	}

	private delegate bool TryParse<T>(string value, out T result);

}
