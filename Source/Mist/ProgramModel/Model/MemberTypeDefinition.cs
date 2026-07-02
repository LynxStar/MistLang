using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Intermediate base for types that participate in inheritance and have
/// method/property declarations: classes, structs, and abstracts.
/// </summary>
internal abstract class MemberTypeDefinition : TypeDefinition, IMemberTypeDefinition
{
    protected MemberTypeDefinition(
        string name,
        TypeDefinitionKind kind,
        string namespaceName,
        Scope scope,
        TypeDefinition? declaringType,
        TypeDeclarationNode declarationNode)
        : base(name, kind, namespaceName, scope, declaringType)
    {
        DeclarationNode = declarationNode;
    }

    /// <summary>
    /// The AST declaration node for this type. Stashed during Phase 1
    /// so Phase 2 can access member declarations without re-walking.
    /// </summary>
    internal TypeDeclarationNode DeclarationNode { get; }

    public List<ITypeReference> BaseTypes { get; } = [];

    public List<MethodDefinition> MethodsList { get; } = [];

    public List<PropertyDefinition> PropertiesList { get; } = [];

    IReadOnlyList<ITypeReference> IMemberTypeDefinition.BaseTypes => BaseTypes;

    IReadOnlyList<IMethodDefinition> IMemberTypeDefinition.Methods => MethodsList;

    IReadOnlyList<IPropertyDefinition> IMemberTypeDefinition.Properties => PropertiesList;
}