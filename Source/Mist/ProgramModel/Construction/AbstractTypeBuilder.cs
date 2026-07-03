using Mist.Common.Collections;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for abstract types. Resolves base abstracts and member
/// signatures (method declarations, property declarations, associated type slots).
/// </summary>
internal sealed class AbstractTypeBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly BaseTypeResolver baseTypeResolver;

    public AbstractTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        baseTypeResolver = new BaseTypeResolver(context, typeResolver);
    }

    /// <summary>
    /// Entry point: resolves the full type definition for an abstract type.
    /// </summary>
    public void Build(AbstractType typeDef)
    {
        BuildDeclaration(typeDef);
        BuildMembers(typeDef);
    }

    /// <summary>
    /// Resolves declaration-level information: base abstracts, modifiers, generics.
    /// </summary>
    private void BuildDeclaration(AbstractType typeDef)
    {
        baseTypeResolver.Resolve(typeDef.DeclarationNode, typeDef);

        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;
        var modifiers = body.TypeBody.Modifiers;

        typeDef.IsReadonly = modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
    }

    /// <summary>
    /// Resolves all member signatures for the abstract type.
    /// </summary>
    private void BuildMembers(AbstractType typeDef)
    {
        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;

        foreach(var member in body.TypeBody.Members)
        {
            ResolveMember(member, typeDef);
        }
    }

    /// <summary>
    /// Dispatches a single abstract member declaration.
    /// </summary>
    private void ResolveMember(MemberDeclarationNode member, AbstractType typeDef)
    {
        switch(member)
        {
            case MethodMemberMemberDeclarationNode methodMember
                when methodMember.Method.Body is MethodNoBodyMethodBodyNode:
                // Abstract method (no body) — will be implemented in a future task.
                break;

            case MethodMemberMemberDeclarationNode methodMember
                when methodMember.Method.Body is MethodBlockBodyMethodBodyNode:
                // Default method (has body) — will be implemented in a future task.
                break;

            case PropertyMemberMemberDeclarationNode propertyMember:
                ResolveProperty(propertyMember.Property, typeDef);
                break;

            case AssociatedTypeMemberMemberDeclarationNode:
            case UnconstrainedAssociatedTypeMemberMemberDeclarationNode:
                // Associated type slots — out of scope for now.
                break;

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownAbstractMember, member, null, member.GetType().Name);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Properties
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a property declaration on an abstract type.
    /// Abstract properties can only have auto accessors (no bodies).
    /// Expression-bodied properties are not allowed.
    /// </summary>
    private void ResolveProperty(PropertyDeclarationNode propertyDecl, AbstractType typeDef)
    {
        switch(propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                ResolveAbstractBlockProperty(blockProp, typeDef);
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                ResolveExpressionBodiedProperty(exprProp, typeDef);
                break;
        }
    }

    private void ResolveAbstractBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, AbstractType typeDef)
    {
        var name = blockProp.Name.Lexeme;
        var type = typeResolver.Resolve(blockProp.Type, typeDef.Scope);
        var isStatic = blockProp.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();

        PropertyAccessor? getter = null;
        PropertyAccessor? setter = null;

        foreach(var accessor in blockProp.Accessors)
        {
            switch(accessor)
            {
                case AutoGetAccessorAccessorDeclarationNode autoGet:
                    ValidateAbstractAccessorModifier(autoGet.AccessorModifier, name, autoGet);
                    getter = new PropertyAccessor { IsAuto = true };
                    break;
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    ValidateAbstractAccessorModifier(blockGet.AccessorModifier, name, blockGet);
                    getter = new PropertyAccessor { Body = new Block(blockGet.Body) };
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    ValidateAbstractAccessorModifier(exprGet.AccessorModifier, name, exprGet);
                    getter = new PropertyAccessor { ExpressionBody = new Expression(exprGet.Expression) };
                    break;

                case AutoSetAccessorAccessorDeclarationNode autoSet:
                    ValidateAbstractAccessorModifier(autoSet.AccessorModifier, name, autoSet);
                    setter = new PropertyAccessor { IsAuto = true };
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    ValidateAbstractAccessorModifier(blockSet.AccessorModifier, name, blockSet);
                    setter = new PropertyAccessor { Body = new Block(blockSet.Body) };
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    ValidateAbstractAccessorModifier(exprSet.AccessorModifier, name, exprSet);
                    setter = new PropertyAccessor { ExpressionBody = new Expression(exprSet.Expression) };
                    break;

                case AutoInitAccessorAccessorDeclarationNode autoInit:
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.AbstractPropertyInitAccessor, autoInit, null, name);
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.AbstractPropertyInitAccessor, blockInit, null, name);
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.AbstractPropertyInitAccessor, exprInit, null, name);
                    break;
            }
        }

        var property = new Property(name, type, isStatic) { Getter = getter, Setter = setter };
        typeDef.PropertiesList.Add(property);
    }

    private void ValidateAbstractAccessorModifier(ModifierNode? modifier, string propertyName, Storm.Runtime.SyntaxNode locationNode)
    {
        if(modifier is not null)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, propertyName);
        }
    }

    private void ResolveExpressionBodiedProperty(ExpressionBodiedPropertyPropertyDeclarationNode exprProp, AbstractType typeDef)
    {
        var name = exprProp.Name.Lexeme;
        var type = typeResolver.Resolve(exprProp.Type, typeDef.Scope);
        var isStatic = exprProp.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();

        var getter = new PropertyAccessor { ExpressionBody = new Expression(exprProp.Body) };
        var property = new Property(name, type, isStatic) { Getter = getter };
        typeDef.PropertiesList.Add(property);
    }
}