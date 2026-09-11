using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

/// <summary>Internal marker for field types carrying optional per-field metadata: a <see cref="PaginateBadge" /> and a <c>When</c> condition (<c>null</c> = unconditional; the field is enabled unless the condition is <c>false</c>).</summary>
internal interface IPaginateFieldTarget {
	PaginateBadge? Badge { get; set; }
	bool? Condition { get; set; }
}

internal sealed record PaginateSortField(string Name, LambdaExpression Selector, Type Type) : IPaginateFieldTarget {
	// These settable auto-props join the record's synthesized equality, but these field records are only ever
	// stored as FrozenDictionary values and never compared — harmless. Not worth converting to a class.
	public PaginateBadge? Badge { get; set; }
	public bool? Condition { get; set; }
}

internal sealed record PaginateSearchField<TEntity>(string Name, Expression<Func<TEntity, string?>> Selector) : IPaginateFieldTarget {
	public PaginateBadge? Badge { get; set; }
	public bool? Condition { get; set; }
}

internal abstract class PaginateFilterField(
	string name,
	Type type,
	IReadOnlySet<PaginateFilterOperator> operators
) : IPaginateFieldTarget {

	public PaginateBadge? Badge { get; set; }
	public bool? Condition { get; set; }

	private readonly static MethodInfo EnumerableContainsMethod =
		PaginateExpressionUtils.GetMethodByParameterCount(typeof(Enumerable), nameof(Enumerable.Contains), 2);

	// The range branch below is reached for exactly two types, so these are two process constants rather than a
	// per-criterion name lookup.
	private readonly static MethodInfo StringCompareToMethod = typeof(string).GetMethod(nameof(IComparable.CompareTo), [typeof(string)])!;

	private readonly static MethodInfo GuidCompareToMethod = typeof(Guid).GetMethod(nameof(IComparable.CompareTo), [typeof(Guid)])!;

	private readonly static MethodInfo StringCompareInvariantMethod =
		typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string), typeof(StringComparison)])!;

	public string Name { get; } = name;

	public Type Type { get; } = Nullable.GetUnderlyingType(type) ?? type;

	public Type ExpressionType { get; } = type;

	public IReadOnlySet<PaginateFilterOperator> Operators { get; } = operators;

	public abstract Expression BuildExpression(ParameterExpression entity, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues);

	/// <summary>
	///     Whether the engine can build <paramref name="filterOperator" /> for this field, asked once at
	///     <c>Build()</c> so an operator list the type cannot carry is a configuration error rather than a 400 on
	///     every request. An operator with no arm has no type precondition to check.
	/// <para>
	///     Two of the three arms mirror their runtime guard exactly. The comparison arm does not, deliberately:
	///     it asks <c>CanCompare</c>, the same <i>range</i> question
	///     <see cref="Model.PaginateFilterOperators" /> asks when deriving an operator set, which wants all four
	///     relational factories — while <c>BuildComparison</c>'s own guard needs only the one being built. So a
	///     type declaring <c>&lt;</c> and <c>&gt;</c> without <c>&lt;=</c> and <c>&gt;=</c> — legal C#, since the
	///     compiler pairs each operator only with its own opposite — can still build <c>$gt</c> at runtime and is
	///     nevertheless refused here. That is the intended reading of "ordered": all four or none, so a shorthand
	///     field and an explicit one answer alike.
	/// </para>
	/// </summary>
	internal bool Supports(PaginateFilterOperator filterOperator) {
		return filterOperator switch {
			PaginateFilterOperator.LessThan or PaginateFilterOperator.LessThanOrEqual
				or PaginateFilterOperator.GreaterThan or PaginateFilterOperator.GreaterThanOrEqual
				or PaginateFilterOperator.Between => CanCompare(Type),
			PaginateFilterOperator.ILike or PaginateFilterOperator.StartsWith => Type == typeof(string),
			PaginateFilterOperator.Contains => Type == typeof(string) || GetEnumerableElementType(ExpressionType) is not null,
			_ => true
		};
	}

	/// <summary>
	///     Whether <c>BuildComparison</c> can express a range over <paramref name="type" />, and the one place that
	///     question is answered — <see cref="PaginateFilterOperators" /> asks it too, when deciding whether a
	///     registered simple type joins the range row. The branches are the builder's own: enums compare on their
	///     underlying integral value, <see langword="string" /> and <see cref="Guid" /> reach a <c>CompareTo</c>
	///     stand-in, and every other type has to carry the relational operators itself. Probing all four through the
	///     expression factory is what keeps this answer and the builder's from drifting: nothing obliges a type to
	///     declare the four together, and the integral primitives declare none of them at all.
	/// </summary>
	internal static bool CanCompare(Type type) {

		var core = Nullable.GetUnderlyingType(type) ?? type;

		if (core.IsEnum || core == typeof(string) || core == typeof(Guid)) return true;

		var operand = Expression.Default(core);

		try {
			Expression.GreaterThan(operand, operand);
			Expression.GreaterThanOrEqual(operand, operand);
			Expression.LessThan(operand, operand);
			Expression.LessThanOrEqual(operand, operand);
		} catch (InvalidOperationException) {
			return false;
		}

		return true;

	}

	protected Expression BuildOperatorExpression(Expression valueExpression, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues) {

		if (!Operators.Contains(criterion.Operator)) {
			throw new PaginateQueryException($"Filter '{Name}' does not support operator '{PaginateFilterParser.GetOperatorToken(criterion.Operator)}'.");
		}

		var expression = criterion.Operator switch {
			PaginateFilterOperator.Eq => BuildEqualityExpression(valueExpression, criterion.Value, context),
			PaginateFilterOperator.In => BuildInExpression(valueExpression, criterion.Value, context, maxFilterValues),
			PaginateFilterOperator.Null => this.BuildNullExpression(valueExpression),
			PaginateFilterOperator.ILike => BuildStringPatternExpression(valueExpression, criterion.Value, false, context),
			PaginateFilterOperator.StartsWith => BuildStringPatternExpression(valueExpression, criterion.Value, true, context),
			PaginateFilterOperator.Contains => BuildContainsExpression(valueExpression, criterion.Value, context, maxFilterValues),
			PaginateFilterOperator.LessThan => BuildComparison(valueExpression, criterion.Value, Expression.LessThan, context),
			PaginateFilterOperator.LessThanOrEqual => BuildComparison(valueExpression, criterion.Value, Expression.LessThanOrEqual, context),
			PaginateFilterOperator.GreaterThan => BuildComparison(valueExpression, criterion.Value, Expression.GreaterThan, context),
			PaginateFilterOperator.GreaterThanOrEqual => BuildComparison(valueExpression, criterion.Value, Expression.GreaterThanOrEqual, context),
			PaginateFilterOperator.Between => BuildBetweenExpression(valueExpression, criterion.Value, context, maxFilterValues),
			_ => throw new PaginateQueryException($"Filter operator '{criterion.Operator}' is not supported.")
		};

		return criterion.Not ? Expression.Not(expression) : expression;

	}

	/// <summary>
	///     Builds the <c>$eq</c> predicate. The guard mirrors the one <c>BuildComparison</c> has always had: a value
	///     can parse cleanly and still have no operator for the factory to use — a plain <c>struct</c> registered
	///     through <c>PaginateTypeSupport</c> declares no <c>op_Equality</c>, and the expression factory answers that
	///     with an <see cref="InvalidOperationException" />. Unguarded it escaped as a 500 for a request the field's
	///     own allow-list had permitted, on the one operator of six that was not covered.
	/// </summary>
	private BinaryExpression BuildEqualityExpression(Expression valueExpression, string value, PaginateExpressionContext context) {

		var constant = ConvertValue(value, valueExpression.Type, context);

		try {
			return Expression.Equal(valueExpression, constant);
		} catch (InvalidOperationException exception) {
			throw new PaginateQueryException($"Filter '{Name}' does not support operator '$eq' for type '{Type.Name}'.", exception);
		}

	}

	/// <summary>
	///     Whether the value is null. The decision uses the <b>declared</b> type rather than the expression's,
	///     because the in-memory leg lifts a value-typed member reached through a navigation to
	///     <see cref="Nullable{T}" /> so it can yield null instead of throwing. Reading the lifted type here
	///     would make <c>$null</c> match a row with no parent in memory while the relational leg — which never
	///     lifts, and answers this from the declared type — matched none, and the two legs must agree. A field
	///     declared non-nullable therefore reports "no row is null" on both, nested or not.
	/// </summary>
	private Expression BuildNullExpression(Expression valueExpression) {
		if (Nullable.GetUnderlyingType(this.ExpressionType) is null && this.ExpressionType.IsValueType) {
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
			converted.SetValue(this.ConvertRawValue(values[i], valueType), i);
		}

		Expression valuesExpression = Expression.Constant(converted, converted.GetType());
		if (context.UseDatabaseFunctions) valuesExpression = PaginateExpressionUtils.ToDatabaseParameter(valuesExpression);

		var containsMethod = EnumerableContainsMethod.MakeGenericMethod(valueType);

		return Expression.Call(containsMethod, valuesExpression, valueExpression);

	}

	private BinaryExpression BuildBetweenExpression(Expression valueExpression, string value, PaginateExpressionContext context, int maxFilterValues) {

		string[] values = SplitValueList(value, maxFilterValues);
		if (values.Length != 2) throw new PaginateQueryException($"Filter '{Name}' requires exactly two '$btw' values.");

		return Expression.AndAlso(
			BuildComparison(valueExpression, values[0], Expression.GreaterThanOrEqual, context),
			BuildComparison(valueExpression, values[1], Expression.LessThanOrEqual, context)
		);

	}

	/// <summary>
	///     Builds one range comparison. The expression factories define no relational operator for enums, strings or
	///     <see cref="Guid" />, so each gets a translatable stand-in rather than the build-time exception that used to
	///     escape as a 500 for a request the field's own operator allow-list had permitted.
	/// </summary>
	private Expression BuildComparison(Expression valueExpression, string value, Func<Expression, Expression, BinaryExpression> comparison, PaginateExpressionContext context) {

		// Numbers, dates and add-on types that define their own operators (NodaTime's Instant, LocalDate) go straight
		// through; only the three families below need help.
		if (!Type.IsEnum && Type != typeof(string) && Type != typeof(Guid)) {
			var constant = ConvertValue(value, valueExpression.Type, context);

			try {
				return comparison(valueExpression, constant);
			} catch (InvalidOperationException exception) {
				// Unreachable through a configuration since Build() refuses the pair, and kept as the backstop it
				// now is: bool, and anything registered through PaginateTypeSupport without operators of its own.
				throw new PaginateQueryException($"Filter '{Name}' does not support comparison operators for type '{Type.Name}'.", exception);
			}
		}

		// Unwrapping the nullable keeps both stand-ins working on the underlying value; the null guard below restores
		// the "a NULL row does not match" behaviour a lifted operator would have given for free.
		var operand = Nullable.GetUnderlyingType(valueExpression.Type) is null
			? valueExpression
			: Expression.Property(valueExpression, "Value");

		Expression compare;

		if (Type.IsEnum) {
			// Compare on the underlying integral type, which is also what the column stores unless the model maps the
			// enum to text — in which case this filter does not translate, exactly as it did not before.
			var underlying = Enum.GetUnderlyingType(Type);
			object? ordinal = Convert.ChangeType(this.ConvertRawValue(value, Type), underlying, CultureInfo.InvariantCulture);

			compare = comparison(Expression.Convert(operand, underlying), ToConstant(ordinal, underlying, context));
		} else {
			// On a relational provider CompareTo translates to a plain SQL comparison, so the ordering is the
			// database's — collation for strings, byte order for Guids. In memory the call really runs, and
			// String.CompareTo reads CultureInfo.CurrentCulture: the same rows and the same filter answer
			// differently depending on the host's culture, so a Swedish deployment disagrees with an American one.
			// An app that opts into request localization -- UseRequestLocalization, which is NOT in the default
			// pipeline -- moves that per caller, from the query string, a cookie or Accept-Language. That arm
			// compares invariantly instead. Guid has no culture to read, so it is the same call on both legs.
			var target = ConvertValue(value, Type, context);

			compare = comparison(
				Type == typeof(string) && !context.UseDatabaseFunctions
					? Expression.Call(StringCompareInvariantMethod, operand, target, Expression.Constant(StringComparison.InvariantCulture))
					: Expression.Call(operand, Type == typeof(string) ? StringCompareToMethod : GuidCompareToMethod, target),
				Expression.Constant(0));
		}

		// Mirrors the pattern operators: SQL already yields false for NULL, the in-memory provider would throw.
		return valueExpression.Type.IsValueType && Nullable.GetUnderlyingType(valueExpression.Type) is null
			? compare
			: Expression.AndAlso(Expression.NotEqual(valueExpression, Expression.Constant(null, valueExpression.Type)), compare);

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
	private Expression ConvertValue(string value, Type targetType, PaginateExpressionContext context) { return ToConstant(this.ConvertRawValue(value, targetType), targetType, context); }

	/// <summary>
	///     Parses one criterion value, refusing a blank one first. A blank used to convert to <c>null</c> wherever
	///     the target was nullable, which is <c>$null</c> spelled implicitly — and the implicit spelling asked the
	///     field's operator allow-list nothing, so a configuration withholding <c>Null</c> answered the null rows
	///     anyway. It also read whichever type reached it, so a nested value-typed member the in-memory rewriter
	///     had lifted to <see cref="Nullable{T}" /> matched rows in memory while every relational provider answered
	///     400. There is one spelling for "no value" now, and it is the declared one. <c>string</c> is untouched:
	///     an empty string is a value, not an absence.
	/// </summary>
	private object? ConvertRawValue(string value, Type targetType) {

		if (targetType != typeof(string) && string.IsNullOrWhiteSpace(value)) {
			throw new PaginateQueryException($"Filter '{Name}' requires a value; use '$null' to match rows with no value.");
		}

		return PaginateValueConverter.Convert(value, targetType, Name);

	}

	/// <summary>Wraps an already-converted value as a constant of <paramref name="targetType" />, parameterised as above.</summary>
	private static Expression ToConstant(object? value, Type targetType, PaginateExpressionContext context) {
		var constant = Expression.Constant(value, targetType);
		return context.UseDatabaseFunctions ? PaginateExpressionUtils.ToDatabaseParameter(constant) : constant;
	}

	/// <summary>
	///     Splits a list criterion on commas. Entries are <b>not</b> trimmed: padding is part of the value, and
	///     trimming here made <c>$in:x</c> and <c>$eq:x</c> mean two different things on a string field.
	/// </summary>
	private string[] SplitValueList(string value, int maxFilterValues) {

		string[] values = value.Split(',', StringSplitOptions.RemoveEmptyEntries);

		return values.Length > maxFilterValues ? throw new PaginateQueryException($"Filter '{Name}' accepts at most {maxFilterValues} values.") : values;

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
		if (!context.UseDatabaseFunctions) valueExpression = PaginateNullSafeRewriter.Rewrite(valueExpression, entity);

		return BuildOperatorExpression(valueExpression, criterion, context, maxFilterValues);
	}

}

internal sealed class PaginateCollectionFilterField<TEntity, TElement>(
	string name,
	Expression<Func<TEntity, IEnumerable<TElement>>> collectionSelector,
	LambdaExpression valueSelector,
	Type type,
	IReadOnlySet<PaginateFilterOperator> operators
) : PaginateFilterField(name, type, operators) {

	private readonly static MethodInfo EnumerableAnyMethod = PaginateExpressionUtils
		.GetMethodByParameterCount(typeof(Enumerable), nameof(Enumerable.Any), 2)
		.MakeGenericMethod(typeof(TElement));

	public override Expression BuildExpression(ParameterExpression entity, PaginateFilterCriterion criterion, PaginateExpressionContext context, int maxFilterValues) {

		var collectionExpression = ParameterReplaceVisitor.Replace(collectionSelector.Body, collectionSelector.Parameters[0], entity);
		var element = Expression.Parameter(typeof(TElement), "item");
		var valueExpression = ParameterReplaceVisitor.Replace(valueSelector.Body, valueSelector.Parameters[0], element);

		if (!context.UseDatabaseFunctions) {
			collectionExpression = PaginateNullSafeRewriter.Rewrite(collectionExpression, entity);
			valueExpression = PaginateNullSafeRewriter.Rewrite(valueExpression, element);
		}

		var predicateBody = BuildOperatorExpression(valueExpression, criterion, context, maxFilterValues);
		var predicate = Expression.Lambda<Func<TElement, bool>>(predicateBody, element);

		Expression any = Expression.Call(EnumerableAnyMethod, collectionExpression, predicate);

		// Any(null, …) throws rather than answering false, so an unloaded or genuinely empty navigation would take
		// down the in-memory leg for a request the database answers with no rows. EF never hands us a null here.
		return context.UseDatabaseFunctions
			? any
			: Expression.AndAlso(Expression.NotEqual(collectionExpression, Expression.Constant(null, collectionExpression.Type)), any);

	}

}

internal sealed class ParameterReplaceVisitor(ParameterExpression source, Expression target) : ExpressionVisitor {

	public static Expression Replace(Expression expression, ParameterExpression source, Expression target) { return new ParameterReplaceVisitor(source, target).Visit(expression); }

	protected override Expression VisitParameter(ParameterExpression node) { return ReferenceEquals(node, source) ? target : base.VisitParameter(node); }

}
