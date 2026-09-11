using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Buffers;

namespace Janzen.Pagination.EntityFrameworkCore.Links;

/// <summary>
///     Framework-agnostic input for building pagination links: the request path and its query parameters.
///     The ASP.NET Core package builds this from an <c>HttpRequest</c>.
/// </summary>
/// <remarks>
///     Two contexts describing the same request compare <b>equal</b>: <c>Equals</c> and <c>GetHashCode</c> are
///     hand-written, because a record's synthesized equality runs <c>QueryParameters</c> through
///     <c>EqualityComparer&lt;T&gt;.Default</c>, which for a list is reference equality. Order is part of the
///     value — it is the order in which the parameters are emitted.
/// </remarks>
/// <param name="Path">The request path the links are built on. See the <see cref="Path" /> property.</param>
/// <param name="QueryParameters">
///     The request's other query parameters. See the <see cref="QueryParameters" /> property.
/// </param>
public sealed record PaginateLinkContext(string Path, IReadOnlyList<KeyValuePair<string, string>> QueryParameters) {

	// Characters no URI path may carry: the C0 control range and DEL, a space, and the ones that would end
	// the path or be re-read as syntax. The controls are here for the reason PaginateInputGuard.Echo strips
	// them — a CR or LF in a path reaches the opt-in Link header writer and splits the header. Deliberately
	// narrow otherwise: everything else is either legal in a path or already percent-encoded by the caller. A
	// SearchValues builds the lookup once rather than per construction, and this runs on every web request.
	private readonly static SearchValues<char> ForbiddenInAPath = SearchValues.Create([
		.. Enumerable.Range(0, 0x20).Select(codePoint => (char)codePoint), '\u007F',
		'?', '#', ' ', '"', '<', '>', '\\', '`', '^', '{', '|', '}'
	]);

	/// <summary>
	///     The request path the links are built on, emitted verbatim before the <c>?</c>, so it must <b>already be
	///     URI-escaped</b>: take <c>PathString.ToUriComponent()</c> in ASP.NET Core, or escape each segment with
	///     <c>Uri.EscapeDataString</c> and join them with <c>/</c> elsewhere. A path holding a character that
	///     cannot appear in one — a space, a control character, or <c>? # " &lt; &gt; \ ` ^ { | }</c> — is refused
	///     here rather than emitted as a link to a different resource.
	/// </summary>
	public string Path {
		get;
		init => field = ValidatePath(value);
	} = ValidatePath(Path);

	/// <summary>
	///     The request's other query parameters, carried onto every link so filters and sorting survive navigation.
	///     Supply keys and values <b>raw</b> — the builder percent-encodes both, so pre-escaping double-encodes them.
	///     Any <c>page</c> entry is dropped and re-added per link; repeat a key to carry a multi-valued parameter.
	///     The whole set is repeated in each of the five links, and again in each rel of the opt-in <c>Link</c>
	///     header, so a long query string is reflected many times over; supply a filtered list where that matters.
	/// </summary>
	public IReadOnlyList<KeyValuePair<string, string>> QueryParameters {
		get;
		init => field = ValidateQueryParameters(value);
	} = ValidateQueryParameters(QueryParameters);

	/// <summary>
	///     Compares two contexts by value: the path ordinally, then the query parameters pairwise, in order.
	///     Written by hand because the synthesized version compares the parameter <b>list</b> by reference and
	///     reports two contexts describing the same request as different.
	/// </summary>
	public bool Equals(PaginateLinkContext? other) {
		if (ReferenceEquals(this, other)) return true;

		return other is not null
			&& string.Equals(this.Path, other.Path, StringComparison.Ordinal)
			&& PaginateStructuralEquality.PairListEquals(this.QueryParameters, other.QueryParameters);
	}

	/// <summary>Hashes the same members <see cref="Equals(PaginateLinkContext)" /> compares, so equal contexts hash equal.</summary>
	public override int GetHashCode() {
		return HashCode.Combine(
			this.Path is null ? 0 : StringComparer.Ordinal.GetHashCode(this.Path),
			PaginateStructuralEquality.PairListHash(this.QueryParameters));
	}

	// Validating in the init accessors is what makes `with` run these too, and the initializers above are what
	// make construction run them. A record's synthesized copy constructor copies every field verbatim — a guard
	// held in one is carried across as "already validated" and the changed member is never looked at, so
	// context with { Path = "/a b" } walked straight past this. Declaring the two positional properties is the
	// only way to reach that path, and it is why their prose sits on the properties rather than on the <param>
	// tags: an explicitly declared property gets no <param> -> summary copy, so the documentation has to be
	// where the consumer hovers.
	private static string ValidatePath(string path) {

		// The name is spelled out: CallerArgumentExpression would report the lowercase positional parameter
		// while the throw below already says Path, and one constructor reporting an argument two ways is the
		// very defect WEB-04 fixes elsewhere in this change.
		ArgumentNullException.ThrowIfNull(path, nameof(Path));

		int offending = path.AsSpan().IndexOfAny(ForbiddenInAPath);

		if (offending >= 0) {
			// Named by code point, never echoed: the offender can be a control character, and this message reaches
			// a log or a console the same way a 400 detail would.
			throw new ArgumentException(
				$"Path contains U+{(int)path[offending]:X4}, which cannot appear in a URI path — the link would "
				+ "address a different resource. Supply an escaped path: PathString.ToUriComponent() in ASP.NET "
				+ "Core, or Uri.EscapeDataString per segment elsewhere.",
				nameof(Path));
		}

		return path;

	}

	private static IReadOnlyList<KeyValuePair<string, string>> ValidateQueryParameters(
		IReadOnlyList<KeyValuePair<string, string>> queryParameters) {

		ArgumentNullException.ThrowIfNull(queryParameters, nameof(QueryParameters));
		return queryParameters;

	}

}
