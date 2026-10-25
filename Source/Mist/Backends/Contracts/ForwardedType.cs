namespace Mist.Backends.Contracts;

/// <summary>
/// <see cref="TypeName"/> is a metadata name, with a nested type written after its encloser and a slash,
/// as in <c>Widget/Part</c>: the facade forwards a nested type through its encloser's row, and the slash
/// is how that row is found.
/// </summary>
internal sealed record ForwardedType(string NamespaceName, string TypeName);