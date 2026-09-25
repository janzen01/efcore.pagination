using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Text;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

/// <summary>
///     The two things done to caller-supplied text before it is used: refusing the one character no provider can
///     store, and bounding what is echoed back when the request is rejected. Both belong above the provider, so
///     every backend answers the same way, and the answer is the documented 400.
/// </summary>
internal static class PaginateInputGuard {

	/// <summary>
	///     How much of a caller-supplied value an error message may repeat. Long enough to recognize what was sent,
	///     short enough that one rejected request cannot turn a 10 000-character filter into a 10 000-character
	///     <c>ProblemDetails</c> detail.
	/// </summary>
	private const int MaxEchoLength = 120;

	private const string Ellipsis = "...";

	/// <summary>
	///     Refuses a value carrying <c>U+0000</c>. A PostgreSQL <c>text</c> value cannot hold one, so the server
	///     rejects the parameter at the protocol boundary with <c>22021</c> — an unhandled provider exception, and a
	///     500 for a request the contract answers with a 400. The rest of C0 is legitimate text that every provider
	///     this library targets accepts, so only the nul byte is refused. <paramref name="subject" /> names what was
	///     being read, so the message reads like the rest of the catalog.
	/// </summary>
	public static void RejectNul(string value, string subject) {

		if (value.Contains('\0', StringComparison.Ordinal)) {
			// ValueInvalid rather than a code of its own: a dedicated member would be a new public surface,
			// and the decision that introduced the codes enumerated the causes it wanted. Worth revisiting
			// if a client ever needs to tell a nul byte apart from any other unusable value.
			throw new PaginateQueryException($"{subject} must not contain a null character.") { Code = PaginateQueryError.ValueInvalid };
		}

	}

	/// <summary>
	///     The form a caller-supplied value takes inside an error message: truncated to a fixed budget and stripped
	///     of control characters. Without the strip a value carrying <c>%0d%0a</c> puts a real line break in the
	///     detail, which a plain-text log sink then renders as a second, forged entry (CWE-117).
	/// </summary>
	public static string Echo(string value) {

		int length = value.Length;
		bool truncated = length > MaxEchoLength;

		if (truncated) {
			length = MaxEchoLength;
			// Never cut a surrogate pair in half: a lone surrogate is not well-formed text, and every sink
			// downstream has its own idea of what to do with one.
			if (char.IsHighSurrogate(value[length - 1])) length--;
		}

		var builder = new StringBuilder(length + Ellipsis.Length);

		foreach (char character in value.AsSpan(0, length)) {
			if (!char.IsControl(character)) builder.Append(character);
		}

		return truncated ? builder.Append(Ellipsis).ToString() : builder.ToString();

	}

}
