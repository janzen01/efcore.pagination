using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Model;
using Janzen.Pagination.NodaTime;

using Microsoft.AspNetCore.Builder;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The shipped trim/AOT annotation contract. The analyzers enabled in <c>Directory.Build.props</c> keep the four
///     packable projects free of unannotated reflection, but they say nothing about the annotations reaching the
///     <i>public</i> members a consumer actually calls — a reflective path could be silenced by annotating a private
///     helper and never surface at the call site. These assertions read the attributes off the built assemblies, so a
///     pair dropped in a refactor is a red test rather than a package that quietly stops warning a trimming consumer.
/// </summary>
public sealed class TrimAnnotationTests {

	public static TheoryData<string> ReflectiveEntryPoints => [
		nameof(PaginateNodaTime.Register),
		nameof(PaginationBuilderNodaTimeExtensions.UseNodaTime),
		nameof(PaginateFilterOperators.For),
		nameof(PaginationRouteHandlerBuilderExtensions.WithPagination)
	];

	[Theory]
	[MemberData(nameof(ReflectiveEntryPoints))]
	public void A_public_reflective_entry_point_warns_a_trimming_consumer(string member) {

		var methods = Members(member);

		Assert.NotEmpty(methods);

		foreach (var method in methods) {
			Assert.True(
				method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>() is not null,
				$"{method.DeclaringType!.Name}.{method} carries no [RequiresUnreferencedCode]");
			Assert.True(
				method.GetCustomAttribute<RequiresDynamicCodeAttribute>() is not null,
				$"{method.DeclaringType!.Name}.{method} carries no [RequiresDynamicCode]");
		}

	}

	/// <summary>
	///     The OpenAPI transformer activates the config provider with <c>ActivatorUtilities.CreateInstance</c>, whose
	///     <c>Type</c> parameter requires <c>PublicConstructors</c>. Nothing else roots that constructor on the no-DI
	///     shape the integration guide recommends, so the requirement has to travel from the type argument the
	///     consumer writes down to the activation: the type parameter of both <c>WithPagination</c> and the generic
	///     attribute, the base attribute's constructor parameter, and the property the transformer reads.
	/// </summary>
	[Fact]
	public void The_provider_constructor_requirement_reaches_the_consumer_s_type_argument() {

		AssertPublicConstructors(typeof(PaginatedQueryAttribute).GetProperty(nameof(PaginatedQueryAttribute.ConfigProviderType))!);

		AssertPublicConstructors(typeof(PaginatedQueryAttribute<>).GetGenericArguments()[0]);

		AssertPublicConstructors(typeof(PaginationRouteHandlerBuilderExtensions)
			.GetMethod(nameof(PaginationRouteHandlerBuilderExtensions.WithPagination))!
			.GetGenericArguments()[0]);

	}

	private static void AssertPublicConstructors(ICustomAttributeProvider target) {

		var annotation = target
			.GetCustomAttributes(typeof(DynamicallyAccessedMembersAttribute), inherit: false)
			.Cast<DynamicallyAccessedMembersAttribute>()
			.SingleOrDefault();

		Assert.True(annotation is not null, $"{target} carries no [DynamicallyAccessedMembers]");
		Assert.True(
			annotation!.MemberTypes.HasFlag(DynamicallyAccessedMemberTypes.PublicConstructors),
			$"{target} does not require PublicConstructors");

	}

	private static MethodInfo[] Members(string name) {
		return [.. new[] {
			typeof(PaginateNodaTime),
			typeof(PaginationBuilderNodaTimeExtensions),
			typeof(PaginateFilterOperators),
			typeof(PaginationRouteHandlerBuilderExtensions)
		}
		.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
		.Where(method => method.Name == name)];
	}

}
