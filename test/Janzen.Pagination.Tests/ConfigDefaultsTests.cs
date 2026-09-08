namespace Janzen.Pagination.Tests;

/// <summary>
///     Shared limits and guards: the explicit <see cref="PaginateConfigDefaults" /> object and the process-wide
///     <see cref="PaginateConfigDefaults.Shared" /> slot, and the order in which the four sources win.
/// </summary>
/// <remarks>
///     <see cref="PaginateConfigDefaults.Shared" /> is process-wide mutable state, so these run in their own
///     non-parallel collection and restore it — the same treatment <c>PaginateLikeDefaults.Strategy</c> gets.
/// </remarks>
[Collection("ConfigDefaults")]
public sealed class ConfigDefaultsTests : IDisposable {

	private readonly PaginateConfigDefaults _original = PaginateConfigDefaults.Shared;

	public void Dispose() { PaginateConfigDefaults.Shared = _original; }

	private static PaginateConfig<Product> Build(Action<PaginateConfigBuilder<Product>> configure) {
		return PaginateConfig<Product>.Create(b => {
			b.Sortable("id", p => p.Id).WithTieBreaker(p => p.Id);
			configure(b);
		});
	}

	private static PaginateConfig<Product> Build(PaginateConfigDefaults defaults, Action<PaginateConfigBuilder<Product>> configure) {
		return PaginateConfig<Product>.Create(defaults, b => {
			b.Sortable("id", p => p.Id).WithTieBreaker(p => p.Id);
			configure(b);
		});
	}

	[Fact]
	public void An_explicit_defaults_object_supplies_the_limits() {

		var config = Build(new PaginateConfigDefaults { DefaultLimit = 25, MaxLimit = 100 }, _ => { });

		Assert.Equal(25, config.DefaultLimit);
		Assert.Equal(100, config.MaxLimit);

	}

	[Fact]
	public void The_shared_slot_supplies_them_too() {

		PaginateConfigDefaults.Shared = new PaginateConfigDefaults { DefaultLimit = 7, MaxLimit = 70 };

		var config = Build(_ => { });

		Assert.Equal(7, config.DefaultLimit);
		Assert.Equal(70, config.MaxLimit);

	}

	[Fact]
	public void The_builder_beats_the_object_which_beats_the_shared_slot_which_beats_the_constant() {

		PaginateConfigDefaults.Shared = new PaginateConfigDefaults {
			DefaultLimit = 1, MaxLimit = 10, MaxSortFields = 1, MaxFilterValues = 1, MaxSearchLength = 11
		};

		var defaults = new PaginateConfigDefaults { DefaultLimit = 2, MaxLimit = 20, MaxSortFields = 2 };

		var config = Build(defaults, b => b.WithLimits(3, 30));

		Assert.Equal(3, config.DefaultLimit);           // the builder
		Assert.Equal(30, config.MaxLimit);              // the builder
		Assert.Equal(2, config.MaxSortFields);          // the object
		Assert.Equal(1, config.MaxFilterValues);        // the shared slot
		Assert.Equal(11, config.MaxSearchLength);       // the shared slot
		Assert.Equal(20, config.MaxFilterConditions);   // the engine's own constant, reached by nothing else

	}

	[Fact]
	public void Naming_one_guard_no_longer_resets_the_others() {

		// The trap this signature change exists for. WithGuards used to default its other three parameters to the
		// engine's constants, so asking for one guard silently discarded three shared ones.
		PaginateConfigDefaults.Shared = new PaginateConfigDefaults { DefaultLimit = 10, MaxLimit = 10, MaxSearchLength = 64, MaxSortFields = 2 };

		var config = Build(b => b.WithGuards(maxFilterValues: 25));

		Assert.Equal(25, config.MaxFilterValues);
		Assert.Equal(64, config.MaxSearchLength);
		Assert.Equal(2, config.MaxSortFields);

	}

	[Fact]
	public void The_new_guards_are_shareable_too() {

		var config = Build(new PaginateConfigDefaults { DefaultLimit = 10, MaxLimit = 10, MinSearchLength = 3, MaxOffset = 5_000 }, _ => { });

		Assert.Equal(3, config.MinSearchLength);
		Assert.Equal(5_000, config.MaxOffset);

	}

	[Fact]
	public void Unlimited_is_deliberately_not_shareable() {

		// There is no MaxRows on the defaults object: an unbounded read is a claim about one resource's size.
		var config = Build(new PaginateConfigDefaults { DefaultLimit = 10, MaxLimit = 10 }, _ => { });

		Assert.Null(config.UnlimitedMaxRows);

	}

	[Fact]
	public void Limits_from_nowhere_are_still_a_configuration_error() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(_ => { }));

		Assert.Equal("Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).", exception.Message);

	}

	[Fact]
	public void Halves_from_different_sources_are_still_checked_against_each_other() {

		// The two halves can now arrive from different places -- here the object brings the default and the
		// shared slot the maximum -- so the pairing check cannot live in WithLimits, which never sees this pair.
		PaginateConfigDefaults.Shared = new PaginateConfigDefaults { MaxLimit = 5 };

		var exception = Assert.Throws<InvalidOperationException>(() => Build(new PaginateConfigDefaults { DefaultLimit = 50 }, _ => { }));

		Assert.Equal("Default limit 50 must not be greater than max limit 5.", exception.Message);

	}

	[Fact]
	public void A_config_does_not_observe_a_later_assignment() {

		PaginateConfigDefaults.Shared = new PaginateConfigDefaults { DefaultLimit = 5, MaxLimit = 50 };
		var config = Build(_ => { });

		PaginateConfigDefaults.Shared = new PaginateConfigDefaults { DefaultLimit = 9, MaxLimit = 90 };

		Assert.Equal(5, config.DefaultLimit);

	}

	[Fact]
	public void A_nonsense_value_in_the_object_is_caught_at_Build() {

		// The builder methods reject at the call site; init accessors cannot, so the check has to run once the
		// value has been resolved. Without it, MaxOffset = -1 refuses every request including page 1.
		Assert.Equal("MaxOffset must not be negative.",
			Assert.Throws<InvalidOperationException>(() => Build(new PaginateConfigDefaults { DefaultLimit = 5, MaxLimit = 5, MaxOffset = -1 }, _ => { })).Message);

		Assert.Equal("MaxSearchLength must be greater than zero.",
			Assert.Throws<InvalidOperationException>(() => Build(new PaginateConfigDefaults { DefaultLimit = 5, MaxLimit = 5, MaxSearchLength = 0 }, _ => { })).Message);

		Assert.Equal("MaxLimit must be greater than zero.",
			Assert.Throws<InvalidOperationException>(() => Build(new PaginateConfigDefaults { DefaultLimit = 5, MaxLimit = 0 }, _ => { })).Message);

	}

	[Fact]
	public void The_shared_slot_refuses_null() {
		Assert.Throws<ArgumentNullException>(() => PaginateConfigDefaults.Shared = null!);
	}
}

/// <summary>Serialises the tests that assign <see cref="PaginateConfigDefaults.Shared" />, which is process-wide.</summary>
[CollectionDefinition("ConfigDefaults", DisableParallelization = true)]
public sealed class ConfigDefaultsCollection;
