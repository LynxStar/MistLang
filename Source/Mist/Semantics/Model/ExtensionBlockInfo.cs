using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// A single registered extension block with its resolved receiver type,
/// member scope, and source metadata.
/// </summary>
public sealed class ExtensionBlockInfo
{
    /// <summary>The resolved receiver type (e.g., ClassType for "string").</summary>
    public required IType ReceiverType { get; init; }

    /// <summary>The receiver parameter name (e.g., "self").</summary>
    public required string ReceiverParameterName { get; init; }

    /// <summary>The scope containing the extension block's member symbols.</summary>
    public required IScope MemberScope { get; init; }

    /// <summary>Type parameters declared on the extension block (empty for non-generic).</summary>
    public required IReadOnlyList<TypeParameterType> TypeParameters { get; init; }

    /// <summary>The containing namespace (null for top-level extensions).</summary>
    public required string? ContainingNamespace { get; init; }

    /// <summary>The AST node for diagnostic reporting.</summary>
    public required Storm.Runtime.SyntaxNode DeclaringNode { get; init; }

    /// <summary>Whether this is a universal extension (receiver type is unknown).</summary>
    public bool IsUniversal => ReceiverType is UnknownType;
}
