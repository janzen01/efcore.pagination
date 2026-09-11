using Janzen.Pagination.AspNetCore.ModelBinding;

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>
///     Which parameters <see cref="PaginateQueryModelBinderProvider" /> claims. It sits at index 0 of
///     <c>MvcOptions.ModelBinderProviders</c> and the factory is first-non-null-wins, so anything it answers
///     for is a parameter no other provider — including the framework's own <c>[ModelBinder]</c> one — can reach.
/// </summary>
public sealed class ModelBinderProviderTests {

	private sealed class Context(Type modelType, BindingInfo bindingInfo) : ModelBinderProviderContext {

		public override BindingInfo BindingInfo { get; } = bindingInfo;

		public override ModelMetadata Metadata { get; } = new EmptyModelMetadataProvider().GetMetadataForType(modelType);

		public override IModelMetadataProvider MetadataProvider { get; } = new EmptyModelMetadataProvider();

		public override IModelBinder CreateBinder(ModelMetadata metadata) { throw new NotSupportedException(); }

	}

	private sealed class OtherBinder : IModelBinder {
		public Task BindModelAsync(ModelBindingContext bindingContext) { return Task.CompletedTask; }
	}

	private static IModelBinder? Resolve(Type modelType, BindingInfo? bindingInfo = null) {
		return new PaginateQueryModelBinderProvider().GetBinder(new Context(modelType, bindingInfo ?? new BindingInfo()));
	}

	[Fact]
	public void A_bare_parameter_is_claimed() { Assert.IsType<PaginateQueryModelBinder>(Resolve(typeof(PaginateQuery))); }

	[Fact]
	public void A_parameter_of_another_type_is_left_alone() { Assert.Null(Resolve(typeof(string))); }

	[Fact]
	public void An_explicit_model_binder_attribute_wins() {

		// [ModelBinder(typeof(...))] is the framework's per-parameter override. Claiming the parameter anyway
		// made it inert for this type application-wide, because this provider is consulted first.
		var binding = new BindingInfo { BinderType = typeof(OtherBinder) };

		Assert.Null(Resolve(typeof(PaginateQuery), binding));

	}

	[Theory]
	[InlineData("Query")]
	[InlineData("Body")]
	public void Every_other_binding_source_is_still_claimed(string source) {

		// A bare parameter on an [ApiController] is inferred as BindingSource.Body, indistinguishable from an
		// explicit [FromBody]. Standing down for Body would turn a working GET into "A non-empty request body
		// is required." — which is why the binding source is deliberately not consulted.
		var binding = new BindingInfo { BindingSource = source == "Query" ? BindingSource.Query : BindingSource.Body };

		Assert.IsType<PaginateQueryModelBinder>(Resolve(typeof(PaginateQuery), binding));

	}

}
