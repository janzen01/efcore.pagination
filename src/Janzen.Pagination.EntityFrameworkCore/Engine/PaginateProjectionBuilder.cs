using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal static class PaginateProjectionBuilder {

	public static Expression<Func<TEntity, TResult>> Build<TEntity, TResult>() { return Cache<TEntity, TResult>.Projection.Value; }

	private static Expression<Func<TEntity, TResult>> Create<TEntity, TResult>() {
		var source = Expression.Parameter(typeof(TEntity), "item");
		var body = BuildObject(source, typeof(TResult), typeof(TEntity).Name, []);
		return Expression.Lambda<Func<TEntity, TResult>>(body, source);
	}

	// `building` holds the (source, target) pairs currently on the stack, which is what makes a self-referencing
	// DTO an error rather than a StackOverflowException. It is a stack, not a history: a pair is removed once its
	// object is built, so two sibling members of the same nested DTO type are still both projected.
	private static NewExpression BuildObject(Expression source, Type targetType, string path, HashSet<(Type Source, Type Target)> building) {

		// List<T> alone used to reach a constructor here: (int capacity) precedes (IEnumerable<T>) in metadata
		// order, so the builder emitted new List<T>(source.Capacity) -- an always-empty list -- and the query
		// paid a join to read the capacity it then discarded. Every sibling collection type already threw.
		if (IsCollection(targetType)) {
			throw new InvalidOperationException($"Cannot automatically project '{path}' into a collection. Use PaginateSelectAsync for sub-collections.");
		}

		if (!building.Add((source.Type, targetType))) {
			throw new InvalidOperationException($"Cannot automatically project '{path}' into '{targetType.Name}': the type is recursive.");
		}

		var constructor = SelectConstructor(targetType);
		var parameters = constructor.GetParameters();
		var arguments = new Expression[parameters.Length];

		for (int i = 0; i < parameters.Length; i++) {
			var parameter = parameters[i];
			var sourceMember = FindSourceMember(source.Type, parameter.Name!, path);
			Expression sourceValue = Expression.MakeMemberAccess(source, sourceMember);
			arguments[i] = BuildArgument(sourceValue, sourceMember, parameter, $"{path}.{sourceMember.Name}", building);
		}

		building.Remove((source.Type, targetType));

		return Expression.New(constructor, arguments);

	}

	private static ConstructorInfo SelectConstructor(Type targetType) {

		var constructors = targetType.GetConstructors();

		if (constructors.Length == 0) {
			throw new InvalidOperationException($"Type '{targetType.Name}' does not expose a public constructor for automatic projection.");
		}

		int arity = constructors.Max(item => item.GetParameters().Length);

		// A parameterless winner builds `new TResult()`, which is valid, translates to SELECT 1 and returns a
		// correct envelope whose every row is all-default. A target with no parameters carries no information by
		// construction, so there is nothing to project into it.
		if (arity == 0) {
			throw new InvalidOperationException($"Type '{targetType.Name}' exposes no public constructor with parameters for automatic projection.");
		}

		var widest = constructors.Where(item => item.GetParameters().Length == arity).ToArray();

		// Type.GetConstructors promises no order, so taking the first would make the columns the API returns a
		// function of the order the constructors happen to be declared in.
		if (widest.Length > 1) {
			throw new InvalidOperationException($"Type '{targetType.Name}' exposes {widest.Length} public constructors with {arity} parameters; automatic projection needs exactly one.");
		}

		return widest[0];

	}

	private static Expression BuildArgument(Expression sourceValue, MemberInfo sourceMember, ParameterInfo parameter, string path, HashSet<(Type Source, Type Target)> building) {

		var targetType = parameter.ParameterType;

		if (CanAssign(sourceValue.Type, targetType)) return ConvertIfNeeded(sourceValue, targetType);

		// Conversions contributed by add-on packages (e.g. NodaTime's Instant -> DateTimeOffset via PaginateTypeSupport).
		var conversion = PaginateTypeSupport.TryBuildProjectionConversion(sourceValue, targetType);
		if (conversion is not null) return conversion;

		if (IsSimpleType(targetType)) {
			throw new InvalidOperationException($"Cannot automatically project '{path}' from '{sourceValue.Type.Name}' to '{targetType.Name}'.");
		}

		var nestedTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		var nestedValue = BuildObject(sourceValue, nestedTargetType, path, building);
		var convertedNestedValue = ConvertIfNeeded(nestedValue, targetType);

		// The guard follows the TARGET parameter rather than the source annotation. EF scaffolding's own default
		// for an optional relationship is a nullable FK behind a non-nullable navigation, so the CLR annotation
		// claims "never null" for a row the database is free to leave without a parent -- and the unguarded
		// projection then threw on the first such row, with a different exception type on each leg. The engine
		// cannot consult EF's model (the projection is cached per (TEntity, TResult), not per DbContext model),
		// so the target's own nullability is the only signal available.
		if (CanBeNull(targetType, parameter) && CanHoldNull(sourceValue.Type)) {
			return Expression.Condition(
				Expression.Equal(sourceValue, Expression.Constant(null, sourceValue.Type)),
				Expression.Constant(null, targetType),
				convertedNestedValue
			);
		}

		if (!CanBeNull(sourceValue.Type, sourceMember)) return convertedNestedValue;

		throw new InvalidOperationException($"Cannot automatically project nullable source '{path}' into non-nullable target parameter '{parameter.Name}'.");

	}

	private static MemberInfo FindSourceMember(Type sourceType, string name, string path) {

		var candidates = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			.Select(property => (Member: (MemberInfo)property, Depth: DeclarationDepth(sourceType, property), Kind: 0))
			.Concat(sourceType.GetFields(BindingFlags.Instance | BindingFlags.Public)
				.Where(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
				.Select(field => (Member: (MemberInfo)field, Depth: DeclarationDepth(sourceType, field), Kind: 1)))
			// A member hidden with `new` is returned alongside the declaration it hides and reflection promises no
			// order between the two, so the most-derived one is picked explicitly. Property before field at the
			// same depth keeps the tie-break the concatenation always had.
			.OrderBy(candidate => candidate.Depth)
			.ThenBy(candidate => candidate.Kind)
			.ToArray();

		if (candidates.Length == 0) {
			throw new InvalidOperationException($"Cannot automatically project '{path}' because source type '{sourceType.Name}' has no public member named '{name}'.");
		}

		var best = candidates[0];

		if (candidates.Length > 1 && candidates[1].Depth == best.Depth && candidates[1].Kind == best.Kind) {
			throw new InvalidOperationException($"Cannot automatically project '{path}' because source type '{sourceType.Name}' declares more than one public member named '{name}'.");
		}

		return best.Member;

	}

	private static int DeclarationDepth(Type sourceType, MemberInfo member) {

		int depth = 0;
		for (var type = sourceType; type is not null && type != member.DeclaringType; type = type.BaseType) depth++;

		return depth;

	}

	private static bool IsCollection(Type type) { return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type); }

	private static bool CanAssign(Type sourceType, Type targetType) {

		if (targetType.IsAssignableFrom(sourceType)) return true;

		var targetUnderlyingType = Nullable.GetUnderlyingType(targetType);
		return targetUnderlyingType is not null && targetUnderlyingType == sourceType;

	}

	private static Expression ConvertIfNeeded(Expression expression, Type targetType) { return expression.Type == targetType ? expression : Expression.Convert(expression, targetType); }

	// Whether the CLR type can carry a null at all -- the precondition for comparing the value against one.
	private static bool CanHoldNull(Type type) { return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null; }

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
		       // Leaves, not composites. Left out, the builder recursed into the struct looking for a constructor to
		       // map and failed on a DTO that merely carried a date.
		       effectiveType == typeof(DateOnly) ||
		       effectiveType == typeof(TimeOnly) ||
		       effectiveType == typeof(TimeSpan) ||
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
