namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     The operator set a field of a given type supports, derived from the type itself. This is what the
///     parameterless <c>Filterable</c> / <c>FilterableMany</c> overloads whitelist, and it is public so the same
///     set can be spelled out, extended or trimmed at a call site that wants the explicit signature:
///     <c>b.Filterable("score", x =&gt; x.Score, [.. PaginateFilterOperators.For&lt;int&gt;(), PaginateFilterOperator.Null])</c>.
/// </summary>
/// <remarks>
///     "All applicable", not "all tokens": the derivation lists what the engine can actually build for that type,
///     so a <see langword="string" /> gets the pattern operators and no ranges, and an <see cref="System.Guid" />
///     gets neither. Ranges are deliberately withheld from <see langword="string" />, <see cref="System.Guid" />,
///     <see langword="char" /> and enums — they translate (the engine has a stand-in for each), but the ordering
///     is the database's collation or byte order rather than anything the caller chose, which is rarely what a
///     range filter is asked for. Pass them explicitly when it is.
///     <see cref="PaginateFilterOperator.Null" /> joins the set exactly when the engine can express it: for
///     reference types always, for value types only through <see cref="System.Nullable{T}" />.
///     A later release may add an operator to one of these rows, which widens every field declared through the
///     shorthand on rebuild; that is the meaning of "all applicable" and any such release says so in its notes.
/// </remarks>
public static class PaginateFilterOperators {

	private readonly static PaginateFilterOperator[] Text = [
		PaginateFilterOperator.Eq,
		PaginateFilterOperator.In,
		PaginateFilterOperator.Null,
		PaginateFilterOperator.StartsWith,
		PaginateFilterOperator.Contains,
		PaginateFilterOperator.ILike
	];

	private readonly static PaginateFilterOperator[] Membership = [PaginateFilterOperator.Eq, PaginateFilterOperator.In];

	private readonly static PaginateFilterOperator[] Range = [
		PaginateFilterOperator.Eq,
		PaginateFilterOperator.In,
		PaginateFilterOperator.GreaterThan,
		PaginateFilterOperator.GreaterThanOrEqual,
		PaginateFilterOperator.LessThan,
		PaginateFilterOperator.LessThanOrEqual,
		PaginateFilterOperator.Between
	];

	private readonly static HashSet<Type> Comparable = [
		typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
		typeof(float), typeof(double), typeof(decimal),
		typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan)
	];

	/// <summary>
	///     The operator set for <typeparamref name="TValue" />. Throws <see cref="ArgumentException" /> for a type the
	///     engine cannot filter on, because the shorthand never guesses — declare such a field with the explicit
	///     operator list instead.
	/// </summary>
	/// <typeparam name="TValue">The filtered value's type, exactly as the field's selector returns it.</typeparam>
	public static PaginateFilterOperator[] For<TValue>() { return Derive(typeof(TValue), nameof(TValue)); }

	/// <summary>
	///     The operator set for <paramref name="type" />, the reflection-typed counterpart of <see cref="For{TValue}" />
	///     and what the parameterless builder overloads call. Throws <see cref="ArgumentException" /> for a type the
	///     engine cannot filter on.
	/// </summary>
	/// <param name="type">The filtered value's type, exactly as the field's selector returns it.</param>
	public static PaginateFilterOperator[] For(Type type) { return Derive(type, nameof(type)); }

	// Shared so each entry point names its own input. The generic one is what the operator-less Filterable
	// shorthand calls, and reporting its failure as "(Parameter 'type')" sent a consumer looking for an argument
	// that call site does not have — the input there is the type argument the selector's return type inferred.
	private static PaginateFilterOperator[] Derive(Type type, string paramName) {

		ArgumentNullException.ThrowIfNull(type, paramName);

		var underlying = Nullable.GetUnderlyingType(type);
		var core = underlying ?? type;

		// Reference types are always nullable; value types only through Nullable<T>. NRT erasure means a
		// non-nullable string still lands here as nullable, which costs nothing: $null then matches no row.
		bool nullable = underlying is not null || !type.IsValueType;

		if (core == typeof(string)) return [.. Text];

		if (core == typeof(bool)) return Close([PaginateFilterOperator.Eq], nullable);

		if (core == typeof(Guid) || core == typeof(char) || core.IsEnum) return Close(Membership, nullable);

		// Registered simple types (NodaTime's Instant, LocalDate, …) join the range row when they define an
		// ordering of their own. The probe is deliberately here rather than a hook an add-on package fills in:
		// what the engine can build is the engine's own knowledge.
		if (Comparable.Contains(core) || (PaginateTypeSupport.IsRegisteredSimpleType(core) && IsOrdered(core))) return Close(Range, nullable);

		throw new ArgumentException(
			$"Filter operators cannot be derived for type '{core.Name}'. Declare the field with an explicit operator list.",
			paramName);

	}

	private static PaginateFilterOperator[] Close(PaginateFilterOperator[] operators, bool nullable) { return nullable ? [.. operators, PaginateFilterOperator.Null] : [.. operators]; }

	/// <summary>
	///     Whether <paramref name="type" /> carries an ordering the engine's comparison builder can actually use.
	///     That means the <b>relational operators</b>, nothing else: the builder reaches for its
	///     <c>CompareTo</c> stand-in only for enums, <see langword="string" /> and <see cref="System.Guid" />, and
	///     sends every other type down <c>Expression.GreaterThan</c>, which needs the operator. A registered type
	///     that implements <see cref="IComparable{T}" /> without operators would otherwise be granted the range
	///     row here, advertise it through the metadata and OpenAPI, and answer 400 to every range request.
	/// </summary>
	private static bool IsOrdered(Type type) { return type.GetMethod("op_LessThan", [type, type]) is not null; }

}
