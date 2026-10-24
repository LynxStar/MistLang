namespace Mist.Backends.Contracts;

internal sealed record ForwardingCandidate(
    string NamespaceName,
    string TypeName,
    bool IsPublic,
    DeclarationOrigin Origin);