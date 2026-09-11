using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Linq.Expressions;
using System.Reflection;

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

		// The exact overload, not merely one of that name: the escape-carrying four-parameter form is the only
		// one that honours the engine's escaping, and the three-parameter sibling would compile just as well.
		Assert.Equal(
			[typeof(DbFunctions), typeof(string), typeof(string), typeof(string)],
			call.Method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

	}

	[Fact]
	public void The_published_portable_strategy_restores_the_default_after_UsePostgreSql() {

		// Without it the default is unreachable once replaced: PortableLikeStrategy is internal sealed, so a
		// composition root that called UsePostgreSql() had no expression that names the strategy it displaced.
		new ServiceCollection().AddPagination(p => p.UsePostgreSql());
		Assert.Equal("ILike", BuildLike().Method.Name);

		PaginateLikeDefaults.Strategy = PaginateLikeDefaults.Portable;

		Assert.Equal("Like", BuildLike().Method.Name);

	}

	[Fact]
	public void Assigning_a_null_strategy_is_refused_at_the_assignment() {

		// The property documents itself as never null and the engine dereferences it without a check, so a null
		// assignment used to surface as an NRE inside query composition on the next request -- far from the
		// mistake. PaginateConfigDefaults.Shared already refuses the same way.
		Assert.Throws<ArgumentNullException>(() => PaginateLikeDefaults.Strategy = null!);
		Assert.NotNull(PaginateLikeDefaults.Strategy);

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

/// <summary>
///     A configuration may carry its own strategy, which is what lets one process serve two providers. These
///     mutate <see cref="PaginateLikeDefaults.Strategy" /> to set up the losing host, so they share the serial
///     collection above.
/// </summary>
[Collection("LikeDefaults")]
public sealed class PerConfigLikeStrategyTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>, IDisposable {

	private readonly IPaginateLikeStrategy _previous = PaginateLikeDefaults.Strategy;

	public void Dispose() { PaginateLikeDefaults.Strategy = _previous; }

	/// <summary>
	///     A consumer-written strategy: <c>EF.Functions.Like</c>'s three-argument form, so its SQL is
	///     distinguishable from either shipped strategy by carrying no <c>ESCAPE</c> clause.
	/// </summary>
	private sealed class NoEscapeLikeStrategy : IPaginateLikeStrategy {

		private readonly static MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
			nameof(DbFunctionsExtensions.Like),
			[typeof(DbFunctions), typeof(string), typeof(string)])!;

		public PaginateFilterOperator? PreferredExampleOperator => null;

		public Expression BuildLike(Expression value, Expression pattern) {
			return Expression.Call(LikeMethod, Expression.Property(null, typeof(EF), nameof(EF.Functions)), value, pattern);
		}

	}

	private static PaginateConfig<Product> ConfigWith(IPaginateLikeStrategy? strategy) {
		return PaginateConfig<Product>.Create(builder => {
			builder
				.WithLimits(defaultLimit: 3, maxLimit: 50)
				.Sortable("id", p => p.Id)
				.WithTieBreaker(p => p.Id)
				.Filterable("name", p => p.Name, PaginateFilterOperator.ILike);
			if (strategy is not null) builder.WithLikeStrategy(strategy);
		});
	}

	private string Sql(PaginateConfig<Product> config) {
		using var context = fixture.CreateContext();
		return SqliteFixture.Products(context).ApplyPaginateFilters(Query.Filter("name", "$ilike:widget"), config).Query.ToQueryString();
	}

	[Fact]
	public void A_configuration_carrying_its_own_strategy_is_unaffected_by_the_process_wide_one() {

		// The shape from the finding: one host registers PostgreSQL, and the other host's queries -- aimed at a
		// provider that has never heard of ILIKE -- stop translating. Without a per-config strategy the second
		// host has no way out at all, because BuildLike is handed no provider to dispatch on.
		new ServiceCollection().AddPagination(p => p.UsePostgreSql());

		Assert.Throws<InvalidOperationException>(() => this.Sql(ConfigWith(null)));

		string sql = this.Sql(ConfigWith(new NoEscapeLikeStrategy()));

		Assert.Contains("LIKE", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("ILIKE", sql, StringComparison.Ordinal);

	}

	[Fact]
	public void A_configuration_without_one_still_composes_exactly_the_portable_pattern_match() {

		// The unchanged path, asserted on the emitted SQL rather than in prose: resolving per query must not
		// alter what a configuration that sets no strategy produces.
		string sql = this.Sql(ConfigWith(null));

		Assert.Contains(@"LIKE @p ESCAPE '\'", sql, StringComparison.Ordinal);

	}

	[Fact]
	public void Configuring_one_does_not_write_the_process_wide_default() {

		var strategy = new NoEscapeLikeStrategy();

		this.Sql(ConfigWith(strategy));

		Assert.NotSame(strategy, PaginateLikeDefaults.Strategy);

	}

	[Fact]
	public void A_null_strategy_is_rejected_at_the_builder() {
		Assert.Throws<ArgumentNullException>(() => PaginateConfig<Product>.Create(builder => builder
			.WithLimits(defaultLimit: 3, maxLimit: 50)
			.WithTieBreaker(p => p.Id)
			.WithLikeStrategy(null!)));
	}

	[Theory]
	[InlineData("$ilike:50%", 4)]
	[InlineData("$ilike:a_b", 5)]
	public async Task A_strategy_omitting_the_escape_argument_loses_the_rows_it_should_match(string criterion, int expected) {

		// What the interface's <remarks> and the integration guide's danger block claim, executed rather than
		// asserted in prose. The engine has already escaped the value, so dropping the ESCAPE clause does not
		// hand the caller's wildcards back — it leaves the escape character in the pattern as literal text the
		// column would have to contain. The match set therefore only ever NARROWS: the row that should match
		// disappears, and nothing can be smuggled through. That is why the mistake is easy to ship and not
		// notice on PostgreSQL and MySQL, whose LIKE takes backslash as its default escape and keeps working.
		await using var context = fixture.CreateContext();

		var escaped = await SqliteFixture.Products(context)
			.PageAsync<ProductDto>(Query.Filter("name", criterion), ConfigWith(null));

		var unescaped = await SqliteFixture.Products(context)
			.PageAsync<ProductDto>(Query.Filter("name", criterion), ConfigWith(new NoEscapeLikeStrategy()));

		Assert.Equal([expected], escaped.Items.Select(item => item.Id));
		Assert.Empty(unescaped.Items);

	}

}

/// <summary>
///     What a third party needs to write a correct strategy: the escape character the engine keyed its escaping
///     off, and the shell that passes it. Neither was obtainable from consumer code — the base was
///     <c>internal abstract</c>, both shipped strategies <c>internal sealed</c>, the character a <c>private
///     const</c> — so a strategy written from the interface alone silently dropped the <c>ESCAPE</c> clause.
///     These touch no process-wide state, so they need no collection.
/// </summary>
public sealed class LikeStrategyExtensibilityTests {

	/// <summary>A strategy of a consumer's own, written the way the guide now tells them to write one.</summary>
	private sealed class DerivedStrategy() : PaginateLikeStrategyBase(LikeMethod) {

		private readonly static MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
			nameof(DbFunctionsExtensions.Like),
			[typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

		public override PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

	}

	private static MethodCallExpression BuildLike(IPaginateLikeStrategy strategy) {
		return Assert.IsAssignableFrom<MethodCallExpression>(
			strategy.BuildLike(Expression.Constant("column"), Expression.Constant("%value%")));
	}

	[Fact]
	public void The_published_escape_character_is_the_one_the_shipped_shell_passes() {

		// Read out of the expression the shell actually builds rather than repeated here, so the published
		// member and the emitted ESCAPE argument cannot drift apart in either direction.
		Assert.Equal(
			PaginateLikeDefaults.EscapeCharacter,
			Assert.IsAssignableFrom<ConstantExpression>(BuildLike(PaginateLikeDefaults.Portable).Arguments[3]).Value);

	}

	[Fact]
	public void The_published_escape_character_is_the_one_the_engine_escapes_with() {

		// The other end of the same contract: what EscapeLikePattern prefixes is what a strategy must declare.
		Assert.Equal($"{PaginateLikeDefaults.EscapeCharacter}%", PaginateExpressionUtils.EscapeLikePattern("%"));

	}

	[Fact]
	public void The_escaping_shell_is_public_so_a_consumer_can_derive_from_it() {

		// InternalsVisibleTo makes the type reachable from this assembly either way, so the assertion is on the
		// accessibility itself — a consumer outside the assembly has to be able to name it and derive from it.
		Assert.True(typeof(PaginateLikeStrategyBase).IsPublic);
		Assert.False(typeof(PaginateLikeStrategyBase).IsSealed);

		// Its constructor is protected, which is the accessibility a base wants: derivable from outside the
		// assembly, never instantiable on its own.
		Assert.All(
			typeof(PaginateLikeStrategyBase).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
			constructor => Assert.True(constructor.IsFamily));

	}

	[Fact]
	public void A_strategy_derived_from_the_shell_gets_the_escape_argument_for_free() {

		var call = BuildLike(new DerivedStrategy());

		Assert.Equal("Like", call.Method.Name);
		Assert.Equal(4, call.Arguments.Count);
		Assert.Equal(PaginateLikeDefaults.EscapeCharacter, Assert.IsAssignableFrom<ConstantExpression>(call.Arguments[3]).Value);

	}

}
