using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;

using Microsoft.Extensions.DependencyInjection;

using System.Reflection;
using System.Runtime.CompilerServices;

namespace Janzen.Pagination.Tests;

/// <summary>
///     A canary, not a behaviour test. The engine resolves framework and provider methods by name and — in
///     three places — by parameter count, from <c>static readonly</c> field initialisers. Every one of them is
///     unambiguous against the pinned dependency set, so nothing is broken today; what the shape costs is the
///     moment it stops being true. An upstream overload addition turns <c>Single(...)</c> into an
///     <c>InvalidOperationException</c> and a <c>GetMethod(...)!</c> into a <c>null</c>, and because the field
///     is a type initialiser the consumer sees a <c>TypeInitializationException</c> naming the <i>type</i>,
///     at first query, in production — while <c>dotnet build</c> and <c>dotnet pack</c> see nothing.
///     <para>
///         Touching each type's initialiser here moves that failure onto the dependency-bump pull request,
///         where a red matrix leg costs nothing to read. It adds no production code and no runtime cost.
///         It mutates <see cref="PaginateLikeDefaults.Strategy" /> to reach the PostgreSQL strategy — the only
///         one of the six resolved against a third-party surface — so it shares the serial collection.
///     </para>
/// </summary>
[Collection("LikeDefaults")]
public sealed class ReflectionResolutionTests : IDisposable {

	private readonly IPaginateLikeStrategy _previous = PaginateLikeDefaults.Strategy;

	public void Dispose() { PaginateLikeDefaults.Strategy = _previous; }

	private static Type NpgsqlStrategyType() {

		// Named through the registration rather than by string: the type is internal to the PostgreSql package,
		// and this is the same route a consumer's UsePostgreSql() takes.
		new ServiceCollection().AddPagination(p => p.UsePostgreSql());

		return PaginateLikeDefaults.Strategy.GetType();

	}

	private static void AssertEveryMethodResolved(Type type) {

		// Runs the initialiser first, so an ambiguous Single(...) surfaces here rather than inside GetValue.
		RuntimeHelpers.RunClassConstructor(type.TypeHandle);

		var fields = type
			.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(MethodInfo))
			.ToArray();

		Assert.NotEmpty(fields);
		Assert.All(fields, field => Assert.NotNull((MethodInfo?)field.GetValue(null)));

	}

	[Fact]
	public void The_engines_reflectively_resolved_methods_all_resolve() {

		// The closed generic matters for the collection field: the initialiser is per constructed type.
		AssertEveryMethodResolved(typeof(PaginateQueryableExtensions));
		AssertEveryMethodResolved(typeof(PaginateExpressionUtils));
		AssertEveryMethodResolved(typeof(PaginateFilterField));
		AssertEveryMethodResolved(typeof(PaginateCollectionFilterField<Product, Review>));

	}

	[Fact]
	public void Both_shipped_like_strategies_resolve_their_pattern_match_method() {

		AssertEveryMethodResolved(PaginateLikeDefaults.Portable.GetType());
		AssertEveryMethodResolved(NpgsqlStrategyType());

	}

}
