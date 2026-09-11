using Janzen.Pagination.AspNetCore.OpenApi;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Janzen.Pagination.Tests;

/// <summary>Guards properties of what the packages actually ship, which only the built assembly can answer.</summary>
public sealed class PackagedAssemblyTests {

	/// <summary>
	///     Microsoft.AspNetCore.OpenApi's own target feeds the documentation file of every project reference into
	///     its XML-comment source generator, which compiled the whole of the core package's documentation into this
	///     assembly's user-string heap — 151 392 bytes of it against 28 084 once the input is withheld, for code
	///     that is unreachable because nothing in <c>src/</c> calls <c>AddOpenApi()</c>. The suppression in the
	///     project file is anchored to that target's name, so an upstream rename would restore the payload
	///     silently; measuring the heap against this assembly's own documentation file is what makes it loud.
	/// </summary>
	[Fact]
	public void The_AspNetCore_assembly_does_not_carry_another_package_s_documentation() {

		var assembly = typeof(PaginatedQueryOperationTransformer).Assembly;
		var ownDocumentation = new FileInfo(Path.ChangeExtension(assembly.Location, ".xml"));

		using var file = File.OpenRead(assembly.Location);
		using var image = new PEReader(file);

		var strings = image.GetMetadataReader().GetHeapSize(HeapIndex.UserString);
		var budget = ownDocumentation.Length * 2;

		Assert.True(strings < budget, $"user-string heap is {strings} bytes, budget {budget}");
	}

}
