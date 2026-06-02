using NodaTime;
using NodaTime.Text;


using System.Globalization;

namespace Janzen.Pagination.EntityFrameworkCore;

internal static class PaginateValueConverter {

	public static object? Convert(string value, Type targetType) {

		var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (type == typeof(string)) return value;

		if (value.IsNullOrWhiteSpace()) {
			return Nullable.GetUnderlyingType(targetType) is not null ? null : throw new PaginateQueryException($"Value for type '{type.Name}' must not be empty.");
		}

		if (type == typeof(Guid)) return Parse<Guid>(value, Guid.TryParse, "GUID");
		if (type == typeof(bool)) return Parse<bool>(value, bool.TryParse, "boolean");
		if (type == typeof(Instant)) return ParseNodaTime(value, InstantPattern.ExtendedIso, "instant");
		if (type == typeof(LocalDate)) return ParseNodaTime(value, LocalDatePattern.Iso, "local date");

		try {

			if (type == typeof(short)) return short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(long)) return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (type == typeof(float)) return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (type == typeof(double)) return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (type == typeof(decimal)) return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
			if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
			if (type == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

			if (type.IsEnum) {
				object parsed = Enum.Parse(type, value, true);
				return Enum.IsDefined(type, parsed) ? parsed : throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");
			}

		} catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) {
			throw new PaginateQueryException($"Value '{value}' is not valid for type '{type.Name}'.");
		}

		throw new PaginateQueryException($"Filtering values of type '{type.Name}' is not supported.");

	}

	private static T Parse<T>(string value, TryParse<T> parser, string displayName) {
		return parser(value, out var parsed)
			? parsed
			: throw new PaginateQueryException($"Value '{value}' is not a valid {displayName}.");
	}

	private static T ParseNodaTime<T>(string value, IPattern<T> pattern, string displayName) {
		var result = pattern.Parse(value);
		return result.Success ? result.Value : throw new PaginateQueryException($"Value '{value}' is not a valid {displayName}.");
	}

	private delegate bool TryParse<T>(string value, out T result);

}
