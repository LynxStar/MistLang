using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal sealed class EnumTypeBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public EnumTypeBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    public void Build(EnumType typeDef)
    {
        var enumDecl = typeDef.DeclarationNode!;

        var modifiers = ModifierBuilder.Build(enumDecl, context.Diagnostics, typeDef.DeclaringType);
        context.Attributes.Build(enumDecl.Attributes, typeDef.DeclaredAttributesList, typeDef.Scope);

        typeDef.Modifiers = modifiers;

        ResolveMembers(enumDecl, typeDef);
        ResolveUnderlyingType(enumDecl, typeDef);
    }

    /// <summary>
    /// An annotation, or a name-only enum's byte, is settled here. A base the variants' written values
    /// decide (enums spec, Requirement 3) is left unset for <see cref="Binding.EnumMemberBinder"/>,
    /// since no value has a type until it binds.
    /// </summary>
    private void ResolveUnderlyingType(EnumDeclarationNode enumDecl, EnumType typeDef)
    {
        if(enumDecl.Type is { } typeAnnotation)
        {
            typeDef.UnderlyingType = scopeResolver.Resolve(typeAnnotation, typeDef.Scope);
        }
        else if(typeDef.MembersList.TrueForAll(member => member.Value is null))
        {
            typeDef.UnderlyingType = scopeResolver.LookupWellKnown(WellKnownTypes.Byte);
        }
    }

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

    private static void AddMember(EnumMemberNode memberNode, EnumType typeDef)
    {
        var name = memberNode.Name.Lexeme;
        var isDefault = memberNode.IsDefault is not null;
        var value = memberNode.Value is { } valueExpr ? new UnresolvedExpression(valueExpr) : null;

        typeDef.MembersList.Add(new EnumMember(name, isDefault, value, memberNode));
    }
}