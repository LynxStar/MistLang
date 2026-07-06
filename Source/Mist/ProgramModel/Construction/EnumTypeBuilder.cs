using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for enum types. Resolves the explicit underlying type annotation
/// (if present) and collects member declarations into <see cref="EnumMember"/> instances.
/// Value expression evaluation is deferred to a later phase.
/// </summary>
internal sealed class EnumTypeBuilder
{
    private readonly TypeReferenceResolver typeResolver;

    public EnumTypeBuilder(TypeReferenceResolver typeResolver)
    {
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Entry point: resolves the enum declaration's type annotation and members.
    /// </summary>
    public void Build(EnumType typeDef)
    {
        var enumDecl = typeDef.DeclarationNode;

        ResolveUnderlyingType(enumDecl, typeDef);
        ResolveMembers(enumDecl, typeDef);
    }

    /// <summary>
    /// Resolves the explicit type annotation if present. When absent, the underlying
    /// type will be inferred during a later phase (expression evaluation).
    /// </summary>
    private void ResolveUnderlyingType(EnumDeclarationNode enumDecl, EnumType typeDef)
    {
        if(enumDecl.Type is { } typeAnnotation)
        {
            typeDef.UnderlyingType = typeResolver.Resolve(typeAnnotation, typeDef.Scope);
        }
    }

    /// <summary>
    /// Walks the member list and creates <see cref="EnumMember"/> instances.
    /// </summary>
    private static void ResolveMembers(EnumDeclarationNode enumDecl, EnumType typeDef)
    {
        if(enumDecl.Members is not { } memberList)
        {
            return;
        }

        AddMember(memberList.EnumMember, typeDef);

        foreach(var member in memberList.EnumMembers)
        {
            AddMember(member, typeDef);
        }
    }

    /// <summary>
    /// Creates and adds a single <see cref="EnumMember"/> from its AST node.
    /// </summary>
    private static void AddMember(EnumMemberNode memberNode, EnumType typeDef)
    {
        var name = memberNode.Name.Lexeme;
        var isDefault = memberNode.IsDefault is not null;
        var value = memberNode.Value is { } valueExpr ? new Expression(valueExpr) : null;

        typeDef.MembersList.Add(new EnumMember(name, isDefault, value));
    }
}