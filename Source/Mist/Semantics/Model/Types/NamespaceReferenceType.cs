using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Internal marker type representing a resolved CLR namespace in expression context.
/// Used during postfix member-access resolution for fully-qualified type references
/// such as <c>Newtonsoft.Json.JsonConvert.SerializeObject(...)</c>, where the identifier
/// chain must traverse namespace boundaries before hitting a type.
/// <para>
/// This type never reaches the backend or the emitter — it is consumed entirely within
/// the semantic model's expression resolution pass. Any leftover <c>NamespaceReferenceType</c>
/// that isn't resolved into a concrete type results in an error diagnostic.
/// </para>
/// </summary>
internal sealed class NamespaceReferenceType : IType
{
    /// <summary>The fully-qualified namespace name (e.g., "Newtonsoft.Json").</summary>
    public string NamespaceName { get; }

    public string DisplayName => NamespaceName;
    public TypeKind Kind => TypeKind.Class; // Placeholder; never observed externally.

    public NamespaceReferenceType(string namespaceName)
    {
        NamespaceName = namespaceName;
    }
}
