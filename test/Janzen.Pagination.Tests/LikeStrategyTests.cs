using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Like;

using Microsoft.Extensions.DependencyInjection;

using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>
///     Marks the tests that mutate <see cref="PaginateLikeDefaults.Strategy" />. That property is a public
///     mutable static, so these must not run alongside anything else that issues a query.
/// </summary>
[CollectionDefinition("LikeDefaults", DisableParallelization = true)]
public sealed class LikeDefaultsCollection;

[Collection("LikeDefaults")]
public sealed class LikeStrategyTests : IDisposable {

	private readonly IPaginateLikeStrategy _previous = PaginateLikeDefaults.Strategy;

	public void Dispose() { PaginateLikeDefaults.Strategy = _previous; }

	// IsAssignableFrom, not IsType: Expression.Call hands back an internal arity-specific subclass.
	private static MethodCallExpression BuildLike() {
		return Assert.IsAssignableFrom<MethodCallExpression>(
			PaginateLikeDefaults.Strategy.BuildLike(Expression.Constant("column"), Expression.Constant("%value%")));
	}

	[Fact]
	public void The_default_strategy_emits_a_portable_like() {

		var call = BuildLike();

		Assert.Equal("Like", call.Method.Name);
		Assert.Equal("DbFunctionsExtensions", call.Method.DeclaringType?.Name);
		Assert.Null(PaginateLikeDefaults.Strategy.PreferredExampleOperator);

	}

	[Fact]
	public void Both_strategies_pass_an_explicit_escape_character() {

		// Without it the engine's escaping of % and _ in user input would have nothing to key off.
		Assert.Equal("\\", Assert.IsAssignableFrom<ConstantExpression>(BuildLike().Arguments[3]).Value);

		new ServiceCollection().AddPagination(p => p.UsePostgreSql());

		Assert.Equal("\\", Assert.IsAssignableFrom<ConstantExpression>(BuildLike().Arguments[3]).Value);

	}

	[Fact]
	public void UsePostgreSql_swaps_in_native_ilike() {

		new ServiceCollection().AddPagination(p => p.UsePostgreSql());

		var call = BuildLike();

		Assert.Equal("ILike", call.Method.Name);
		Assert.Equal("NpgsqlDbFunctionsExtensions", call.Method.DeclaringType?.Name);
		Assert.Equal(PaginateFilterOperator.ILike, PaginateLikeDefaults.Strategy.PreferredExampleOperator);

	}

	[Fact]
	public void The_strategy_is_process_wide_not_per_configuration() {

		// Configs built before the swap pick it up too -- there is no per-config copy of the strategy.
		var before = PaginateLikeDefaults.Strategy;

		new ServiceCollection().AddPagination(p => p.UsePostgreSql());

		Assert.NotSame(before, PaginateLikeDefaults.Strategy);
		Assert.Equal("ILike", BuildLike().Method.Name);

	}

}
