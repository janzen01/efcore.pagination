using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.PostgreSql.Like;

// Emits PostgreSQL's native ILIKE for true case-insensitive search.
internal sealed class NpgsqlLikeStrategy() : PaginateLikeStrategyBase(ILikeMethod) {

	// Read off a compiled call rather than looked up by name and arity: the compiler picks the overload, so a
	// future provider release adding another four-parameter ILike cannot turn this into a startup-time
	// TypeInitializationException naming this class instead of the overload that appeared. The sample in
	// docs/src/integrations/postgresql/ shows a consumer strategy the same way.
	private readonly static MethodInfo ILikeMethod = ResolveILike();

	private static MethodInfo ResolveILike() {

		// The local's declared type is what the lambda is converted to; a cast in its place would say the same
		// thing less clearly. Held in a local rather than a field so the tree is collected once the MethodInfo
		// is read -- only the handle needs to outlive this call.
		Expression<Func<string, string, bool>> call =
			(value, pattern) => EF.Functions.ILike(value, pattern, PaginateLikeDefaults.EscapeCharacter);

		return ((MethodCallExpression)call.Body).Method;

	}

	public override PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

}
