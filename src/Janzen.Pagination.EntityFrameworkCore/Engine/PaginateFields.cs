using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal sealed record PaginateSortField(string Name, LambdaExpression Selector, Type Type);

internal sealed record PaginateSearchField<TEntity>(string Name, Expression<Func<TEntity, string?>> Selector);

internal abstract class PaginateFilterField(
	string name,
	Type type,
	IReadOnlySet<PaginateFilterOperator> operators
) {

	protected readonly static MethodInfo EnumerableContainsMethod = typeof(Enumerable)
		.GetMethods()
		.Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2);

	public string Name { get; } = name;

	public Type Type { get; } = Nullable.GetUnderlyingType(type) ?? type;

	public Type ExpressionType { get; } = type;

	public IReadOnlySet<PaginateFilterOperator> Operators { get; } = operators;

	public abstract Expression BuildExpression(ParameterExpression entity, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues);

	protected Expression BuildOperatorExpression(Expression valueExpression, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues) {

		if (!Operators.Contains(criterion.Operator)) {
			throw new PaginateQueryException($"Filter '{Name}' does not support operator '{PaginateFilterParser.GetOperatorToken(criterion.Operator)}'.");
		}

		var expression = criterion.Operator switch {
			PaginateFilterOperator.Eq => BuildEqualityExpression(valueExpression, criterion.Value, context),
			PaginateFilterOperator.In => BuildInExpression(valueExpression, criterion.Value, context, maxFilterValues),
			PaginateFilterOperator.Null => BuildNullExpression(valueExpression),
			PaginateFilterOperator.ILike => BuildStringPatternExpression(valueExpression, criterion.Value, false, context),
			PaginateFilterOperator.StartsWith => BuildStringPatternExpression(valueExpression, criterion.Value, true, context),
			PaginateFilterOperator.Contains => BuildContainsExpression(valueExpression, criterion.Value, context, maxFilterValues),
			PaginateFilterOperator.LessThan => Expression.LessThan(valueExpression, ConvertValue(criterion.Value, valueExpression.Type, context)),
			PaginateFilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(valueExpression, ConvertValue(criterion.Value, valueExpression.Type, context)),
			PaginateFilterOperator.GreaterThan => Expression.GreaterThan(valueExpression, ConvertValue(criterion.Value, valueExpression.Type, context)),
			PaginateFilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(valueExpression, ConvertValue(criterion.Value, valueExpression.Type, context)),
			PaginateFilterOperator.Between => BuildBetweenExpression(valueExpression, criterion.Value, context, maxFilterValues),
			_ => throw new PaginateQueryException($"Filter operator '{criterion.Operator}' is not supported.")
		};

		return criterion.Not ? Expression.Not(expression) : expression;

	}

	private BinaryExpression BuildEqualityExpression(Expression valueExpression, string value, PaginateExpressionContext context) { return Expression.Equal(valueExpression, ConvertValue(value, valueExpression.Type, context)); }

	private static Expression BuildNullExpression(Expression valueExpression) {
		if (Nullable.GetUnderlyingType(valueExpression.Type) is null && valueExpression.Type.IsValueType) {
			return Expression.Constant(false);
		}

		return Expression.Equal(valueExpression, Expression.Constant(null, valueExpression.Type));
	}

	private MethodCallExpression BuildInExpression(Expression valueExpression, string value, PaginateExpressionContext context, int maxFilterValues) {

		string[] values = SplitValueList(value, maxFilterValues);
		if (values.Length == 0) throw new PaginateQueryException($"Filter '{Name}' requires at least one '$in' value.");

		var valueType = valueExpression.Type;
		var converted = Array.CreateInstance(valueType, values.Length);

		for (int i = 0; i < values.Length; i++) {
			converted.SetValue(PaginateValueConverter.Convert(values[i], valueType), i);
		}

		Expression valuesExpression = Expression.Constant(converted, converted.GetType());
		if (context.UseDatabaseFunctions) valuesExpression = PaginateExpressionUtils.ToDatabaseParameter(valuesExpression);

		var containsMethod = EnumerableContainsMethod.MakeGenericMethod(valueType);

		return Expression.Call(containsMethod, valuesExpression, valueExpression);

	}

	private BinaryExpression BuildBetweenExpression(Expression valueExpression, string value, PaginateExpressionContext context, int maxFilterValues) {

		string[] values = SplitValueList(value, maxFilterValues);
		if (values.Length != 2) throw new PaginateQueryException($"Filter '{Name}' requires exactly two '$btw' values.");

		var lower = ConvertValue(values[0], valueExpression.Type, context);
		var upper = ConvertValue(values[1], valueExpression.Type, context);

		return Expression.AndAlso(
			Expression.GreaterThanOrEqual(valueExpression, lower),
			Expression.LessThanOrEqual(valueExpression, upper)
		);

	}

	private Expression BuildContainsExpression(Expression valueExpression, string value, PaginateExpressionContext context, int maxFilterValues) {

		if (Type == typeof(string)) return BuildStringPatternExpression(valueExpression, value, false, context);

		var elementType = GetEnumerableElementType(valueExpression.Type);
		if (elementType is null) throw new PaginateQueryException($"Filter '{Name}' supports '$contains' only for string or collection fields.");

		string[] values = SplitValueList(value, maxFilterValues);
		if (values.Length == 0) throw new PaginateQueryException($"Filter '{Name}' requires at least one '$contains' value.");

		var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
		var collectionExpression = valueExpression.Type == enumerableType
			? valueExpression
			: Expression.Convert(valueExpression, enumerableType);

		var containsMethod = EnumerableContainsMethod.MakeGenericMethod(elementType);

		var aggregate = values
			.Select(rawValue => ConvertValue(rawValue, elementType, context))
			.Select(itemExpression => Expression.Call(containsMethod, collectionExpression, itemExpression))
			.Aggregate<Expression, Expression?>(null, (current, containsExpression) => current is null
				? containsExpression
				: Expression.AndAlso(current, containsExpression));

		if (!valueExpression.Type.IsValueType || Nullable.GetUnderlyingType(valueExpression.Type) is not null) {
			aggregate = Expression.AndAlso(
				Expression.NotEqual(valueExpression, Expression.Constant(null, valueExpression.Type)),
				aggregate!
			);
		}

		return aggregate!;

	}

	private BinaryExpression BuildStringPatternExpression(Expression valueExpression, string value, bool startsWith, PaginateExpressionContext context) {

		if (Type != typeof(string)) throw new PaginateQueryException($"Filter '{Name}' supports string pattern operators only for string fields.");

		var notNull = Expression.NotEqual(valueExpression, Expression.Constant(null, valueExpression.Type));

		Expression patternExpression;

		if (context.UseDatabaseFunctions) {
			string escaped = PaginateExpressionUtils.EscapeLikePattern(value);
			var pattern = PaginateExpressionUtils.ToDatabaseParameter(Expression.Constant(startsWith ? $"{escaped}%" : $"%{escaped}%"));
			patternExpression = context.LikeStrategy.BuildLike(valueExpression, pattern);
		} else {
			patternExpression = PaginateExpressionUtils.BuildInMemoryStringMatchExpression(valueExpression, value, startsWith);
		}

		return Expression.AndAlso(notNull, patternExpression);

	}

	/// <summary>
	///     Converts a raw string value to a constant of the target type, optionally wrapped in
	///     <see cref="EF.Parameter{T}" /> for plan reuse.
	/// </summary>
	private static Expression ConvertValue(string value, Type targetType, PaginateExpressionContext context) {
		var constant = Expression.Constant(PaginateValueConverter.Convert(value, targetType), targetType);
		return context.UseDatabaseFunctions ? PaginateExpressionUtils.ToDatabaseParameter(constant) : constant;
	}

	private string[] SplitValueList(string value, int maxFilterValues) {
		string[] values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		if (values.Length > maxFilterValues) {
			throw new PaginateQueryException($"Filter '{Name}' accepts at most {maxFilterValues} values.");
		}

		return values;
	}

	private static Type? GetEnumerableElementType(Type type) {

		if (type == typeof(string)) return null;

		if (type.IsArray) return type.GetElementType();

		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];

		return type
			.GetInterfaces()
			.Where(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			.Select(item => item.GetGenericArguments()[0])
			.FirstOrDefault();

	}

}

internal sealed class PaginateScalarFilterField<TEntity, TValue>(
	string name,
	Expression<Func<TEntity, TValue>> selector,
	Type type,
	IReadOnlySet<PaginateFilterOperator> operators
) : PaginateFilterField(name, type, operators) {

	public override Expression BuildExpression(ParameterExpression entity, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues) {
		var valueExpression = ParameterReplaceVisitor.Replace(selector.Body, selector.Parameters[0], entity);
		return this.BuildOperatorExpression(valueExpression, criterion, context, maxFilterValues);
	}

}

internal sealed class PaginateCollectionFilterField<TEntity, TElement>(
	string name,
	Expression<Func<TEntity, IEnumerable<TElement>>> collectionSelector,
	LambdaExpression valueSelector,
	Type type,
	IReadOnlySet<PaginateFilterOperator> operators
) : PaginateFilterField(name, type, operators) {

	private readonly static MethodInfo EnumerableAnyMethod = typeof(Enumerable)
		.GetMethods()
		.Single(method => method.Name == nameof(Enumerable.Any) && method.GetParameters().Length == 2)
		.MakeGenericMethod(typeof(TElement));

	public override Expression BuildExpression(ParameterExpression entity, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues) {

		var collectionExpression = ParameterReplaceVisitor.Replace(collectionSelector.Body, collectionSelector.Parameters[0], entity);
		var element = Expression.Parameter(typeof(TElement), "item");
		var valueExpression = ParameterReplaceVisitor.Replace(valueSelector.Body, valueSelector.Parameters[0], element);
		var predicateBody = this.BuildOperatorExpression(valueExpression, criterion, context, maxFilterValues);
		var predicate = Expression.Lambda<Func<TElement, bool>>(predicateBody, element);

		return Expression.Call(EnumerableAnyMethod, collectionExpression, predicate);

	}

}

internal sealed class ParameterReplaceVisitor(ParameterExpression source, Expression target) : ExpressionVisitor {

	public static Expression Replace(Expression expression, ParameterExpression source, Expression target) { return new ParameterReplaceVisitor(source, target).Visit(expression); }

	protected override Expression VisitParameter(ParameterExpression node) { return ReferenceEquals(node, source) ? target : base.VisitParameter(node); }

}
