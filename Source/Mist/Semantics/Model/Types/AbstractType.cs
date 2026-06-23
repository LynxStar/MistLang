using Mist.Semantics.Contracts;
using Storm.Runtime;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined abstract type that unifies the former interface and trait concepts.
/// Declares slots (members without bodies that must be filled by implementers) and
/// optional default implementations (methods with bodies).
/// </summary>
public sealed class AbstractType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Abstract;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }

    /// <summary>Parent abstract types this type inherits from.</summary>
    public IReadOnlyList<IType> ParentAbstractTypes { get; internal set; } = Array.Empty<IType>();

    /// <summary>Type parameters declared on this abstract type.</summary>
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

    /// <summary>Whether this abstract type is declared as readonly.</summary>
    public bool IsReadonly { get; internal set; }

    /// <summary>Whether a cycle was detected in the inheritance chain.</summary>
    public bool IsCyclic { get; internal set; }

    /// <summary>
    /// Method symbols that have default implementation bodies.
    /// Key: method name, Value: the declaring syntax node (MethodDeclarationNode).
    /// </summary>
    public Dictionary<string, SyntaxNode> DefaultMethods { get; } = new();

    /// <summary>
    /// Method names that have default implementations.
    /// Used by the emitter to decide whether to generate forwarding stubs.
    /// </summary>
    public HashSet<string> DefaultMethodNames { get; } = new();

    public AbstractType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
    }
}
