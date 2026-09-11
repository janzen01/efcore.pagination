using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

/// <summary>
///     Rewrites a member-access chain that crosses a navigation — <c>x =&gt; x.Author.Name</c> — into a form that
///     yields <see langword="null" /> instead of throwing when an intermediate is <see langword="null" />.
/// </summary>
/// <remarks>
///     Only the plain-<see cref="IQueryable{T}" /> leg needs this. A relational provider compiles the same selector
///     to a LEFT JOIN, where a missing row makes the whole expression NULL and the surrounding predicate simply
///     false; the in-memory leg compiles it to a field dereference and throws a
///     <see cref="NullReferenceException" />. Rewriting to the conditional form is what makes the two legs answer
///     the same question — including for <c>$null</c> on a <b>reference-typed</b> member, which a row with no author
///     *does* match on both, because the joined column is NULL. Guarding the surrounding predicate instead would
///     have answered "no" to that one and quietly disagreed with every database.
///     A value-typed member is a different case: the lift to <see cref="Nullable{T}" /> exists only so the
///     expression has somewhere to put "absent", and <c>PaginateFilterField.BuildNullExpression</c> deliberately
///     ignores it, deciding from the field's declared type instead. Otherwise <c>$null</c> would match here and
///     match nothing on the relational leg, which reads the same declared type.
///     Anything that is not a plain chain rooted at the parameter (a method call, a computed expression, a captured
///     variable) is returned untouched: there is nothing to guard that would not also change what it evaluates.
/// </remarks>
internal static class PaginateNullSafeRewriter {

	/// <summary>
	///     The null-safe form of <paramref name="body" />, or <paramref name="body" /> itself when it crosses no
	///     nullable intermediate. A value-typed result comes back lifted to <see cref="Nullable{T}" />, so a caller
	///     must read the returned expression's type rather than assume the member's own.
	/// </summary>
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	public static Expression Rewrite(Expression body, ParameterExpression root) {

		// The chain, outermost first on the way down and reversed into evaluation order as it goes.
		List<MemberExpression> chain = [];

		for (var node = body; node is MemberExpression member; node = member.Expression) {
			if (member.Expression is null) return body;   // static member: nothing above it can be null

			chain.Insert(0, member);

			if (ReferenceEquals(member.Expression, root)) return Build(chain, root) ?? body;
		}

		return body;

	}

	/// <summary>
	///     The null-safe counterpart of <paramref name="selector" /> as a lambda over the same parameter, for the
	///     call sites that pass a selector on rather than splicing its body. Returns <paramref name="selector" />
	///     unchanged when there is nothing to guard.
	/// </summary>
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	public static LambdaExpression Rewrite(LambdaExpression selector) {

		var parameter = selector.Parameters[0];
		var body = Rewrite(selector.Body, parameter);

		return ReferenceEquals(body, selector.Body) ? selector : Expression.Lambda(body, parameter);

	}

	/// <summary>Builds the guarded form, or <see langword="null" /> when no intermediate in the chain can be null.</summary>
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	private static Expression? Build(List<MemberExpression> chain, ParameterExpression root) {

		// Every step but the last is an intermediate, and only a reference-typed one can be null — a chain
		// through structs needs neither a guard nor lifting.
		Expression? guard = null;
		Expression current = root;

		for (int index = 0; index < chain.Count - 1; index++) {
			current = Expression.MakeMemberAccess(current, chain[index].Member);

			if (current.Type.IsValueType && Nullable.GetUnderlyingType(current.Type) is null) continue;

			var notNull = Expression.NotEqual(current, Expression.Constant(null, current.Type));

			// AndAlso short-circuits, which is what keeps the guard from dereferencing a null itself: the second
			// test only runs once the first has said the intermediate is there.
			guard = guard is null ? notNull : Expression.AndAlso(guard, notNull);
		}

		if (guard is null) return null;

		var access = Expression.MakeMemberAccess(current, chain[^1].Member);

		var lifted = access.Type.IsValueType && Nullable.GetUnderlyingType(access.Type) is null
			? typeof(Nullable<>).MakeGenericType(access.Type)
			: access.Type;

		return Expression.Condition(
			guard,
			lifted == access.Type ? access : Expression.Convert(access, lifted),
			Expression.Constant(null, lifted));

	}

}
