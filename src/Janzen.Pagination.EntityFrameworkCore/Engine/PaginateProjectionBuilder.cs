using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateProjectionBuilder {

	public static Expression<Func<TEntity, TResult>> Build<TEntity, TResult>() { return Cache<TEntity, TResult>.Projection.Value; }

	private static Expression<Func<TEntity, TResult>> Create<TEntity, TResult>() {
		var source = Expression.Parameter(typeof(TEntity), "item");
		var body = BuildObject(source, typeof(TResult), typeof(TEntity).Name);
		return Expression.Lambda<Func<TEntity, TResult>>(body, source);
	}

	private static NewExpression BuildObject(Expression source, Type targetType, string path) {

		var constructor = targetType
			                  .GetConstructors()
			                  .OrderByDescending(item => item.GetParameters().Length)
			                  .FirstOrDefault() ??
		                  throw new InvalidOperationException($"Type '{targetType.Name}' does not expose a public constructor for automatic projection.");

		var parameters = constructor.GetParameters();
		var arguments = new Expression[parameters.Length];

		for (int i = 0; i < parameters.Length; i++) {
			var parameter = parameters[i];
			var sourceMember = FindSourceMember(source.Type, parameter.Name!, path);
			Expression sourceValue = Expression.MakeMemberAccess(source, sourceMember);
			arguments[i] = BuildArgument(sourceValue, sourceMember, parameter, $"{path}.{sourceMember.Name}");
		}

		return Expression.New(constructor, arguments);

	}

	private static Expression BuildArgument(Expression sourceValue, MemberInfo sourceMember, ParameterInfo parameter, string path) {

		var targetType = parameter.ParameterType;

		if (CanAssign(sourceValue.Type, targetType)) return ConvertIfNeeded(sourceValue, targetType);

		// Conversions contributed by add-on packages (e.g. NodaTime's Instant -> DateTimeOffset via PaginateTypeSupport).
		var conversion = PaginateTypeSupport.TryBuildProjectionConversion(sourceValue, targetType);
		if (conversion is not null) return conversion;

		if (IsSimpleType(targetType)) {
			throw new InvalidOperationException($"Cannot automatically project '{path}' from '{sourceValue.Type.Name}' to '{targetType.Name}'.");
		}

		var nestedTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		var nestedValue = BuildObject(sourceValue, nestedTargetType, path);
		var convertedNestedValue = ConvertIfNeeded(nestedValue, targetType);

		if (!CanBeNull(sourceValue.Type, sourceMember)) return convertedNestedValue;

		if (!CanBeNull(targetType, parameter)) {
			throw new InvalidOperationException($"Cannot automatically project nullable source '{path}' into non-nullable target parameter '{parameter.Name}'.");
		}

		return Expression.Condition(
			Expression.Equal(sourceValue, Expression.Constant(null, sourceValue.Type)),
			Expression.Constant(null, targetType),
			convertedNestedValue
		);

	}

	private static MemberInfo FindSourceMember(Type sourceType, string name, string path) {

		var properties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			.Cast<MemberInfo>();

		var fields = sourceType.GetFields(BindingFlags.Instance | BindingFlags.Public)
			.Where(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
			.Cast<MemberInfo>();

		return properties
			       .Concat(fields)
			       .FirstOrDefault() ??
		       throw new InvalidOperationException($"Cannot automatically project '{path}' because source type '{sourceType.Name}' has no public member named '{name}'.");

	}

	private static bool CanAssign(Type sourceType, Type targetType) {

		if (targetType.IsAssignableFrom(sourceType)) return true;

		var targetUnderlyingType = Nullable.GetUnderlyingType(targetType);
		return targetUnderlyingType is not null && targetUnderlyingType == sourceType;

	}

	private static Expression ConvertIfNeeded(Expression expression, Type targetType) { return expression.Type == targetType ? expression : Expression.Convert(expression, targetType); }

	private static bool CanBeNull(Type type, MemberInfo member) {

		if (Nullable.GetUnderlyingType(type) is not null) return true;
		if (type.IsValueType) return false;

		var context = new NullabilityInfoContext();

		return member switch {
			PropertyInfo property => context.Create(property).ReadState != NullabilityState.NotNull,
			FieldInfo field => context.Create(field).ReadState != NullabilityState.NotNull,
			_ => true
		};

	}

	private static bool CanBeNull(Type type, ParameterInfo parameter) {

		if (Nullable.GetUnderlyingType(type) is not null) return true;
		if (type.IsValueType) return false;

		var context = new NullabilityInfoContext();
		return context.Create(parameter).ReadState != NullabilityState.NotNull;

	}

	private static bool IsSimpleType(Type type) {

		var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

		return effectiveType.IsPrimitive ||
		       effectiveType.IsEnum ||
		       effectiveType == typeof(string) ||
		       effectiveType == typeof(Guid) ||
		       effectiveType == typeof(decimal) ||
		       effectiveType == typeof(DateTime) ||
		       effectiveType == typeof(DateTimeOffset) ||
		       PaginateTypeSupport.IsRegisteredSimpleType(effectiveType);

	}

	// The projection only depends on the (TEntity, TResult) pair, so it is built once per closed generic and reused.
	// Lazy rather than a plain static field: building it can fail on an unprojectable DTO, and a throwing field
	// initializer would reach the caller as TypeInitializationException with the real message one level down.
	// Lazy's default mode keeps the same build-once guarantee and rethrows the original exception unwrapped.
	private static class Cache<TEntity, TResult> {

		public readonly static Lazy<Expression<Func<TEntity, TResult>>> Projection = new(Create<TEntity, TResult>);

	}

}
