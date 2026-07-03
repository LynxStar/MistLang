using Mist.Common.Collections;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves property declarations into <see cref="Property"/> instances.
/// A single code path handles both concrete and abstract contexts,
/// branching on <paramref name="isAbstract"/> for validation differences.
/// </summary>
internal sealed class PropertyBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;

    public PropertyBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves a property declaration on any member-bearing type.
    /// </summary>
    public void Resolve(PropertyDeclarationNode propertyDecl, MemberType typeDef, bool isAbstract)
    {
        switch(propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                ResolveBlockProperty(blockProp, typeDef, isAbstract);
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                ResolveExpressionBodiedProperty(exprProp, typeDef);
                break;
        }
    }

    private void ResolveExpressionBodiedProperty(ExpressionBodiedPropertyPropertyDeclarationNode exprProp, MemberType typeDef)
    {
        var name = exprProp.Name.Lexeme;
        var type = typeResolver.Resolve(exprProp.Type, typeDef.Scope);
        var isStatic = exprProp.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();

        var getter = new PropertyAccessor { ExpressionBody = new Expression(exprProp.Body) };
        var property = new Property(name, type, isStatic) { Getter = getter };
        typeDef.PropertiesList.Add(property);
    }

    private void ResolveBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, MemberType typeDef, bool isAbstract)
    {
        var name = blockProp.Name.Lexeme;
        var type = typeResolver.Resolve(blockProp.Type, typeDef.Scope);
        var isStatic = blockProp.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();

        PropertyAccessor? getter = null;
        PropertyAccessor? setter = null;


        //Todo this really should be structured better at the grammar level.
        foreach(var accessor in blockProp.Accessors)
        {
            getter = accessor switch
            {
                AutoGetAccessorAccessorDeclarationNode autoGet => BuildGetter(autoGet.AccessorModifier, name, autoGet, isAbstract, isAuto: true, body: null, expression: null),
                BlockGetAccessorAccessorDeclarationNode blockGet => BuildGetter(blockGet.AccessorModifier, name, blockGet, isAbstract, isAuto: false, body: blockGet.Body, expression: null),
                ExpressionGetAccessorAccessorDeclarationNode exprGet => BuildGetter(exprGet.AccessorModifier, name, exprGet, isAbstract, isAuto: false, body: null, expression: exprGet.Expression),
                _ => getter,
            };

            setter = accessor switch
            {
                AutoSetAccessorAccessorDeclarationNode autoSet => BuildSetter(autoSet.AccessorModifier, name, autoSet, isAbstract, isAuto: true, isInit: false, body: null, expression: null),
                BlockSetAccessorAccessorDeclarationNode blockSet => BuildSetter(blockSet.AccessorModifier, name, blockSet, isAbstract, isAuto: false, isInit: false, body: blockSet.Body, expression: null),
                ExpressionSetAccessorAccessorDeclarationNode exprSet => BuildSetter(exprSet.AccessorModifier, name, exprSet, isAbstract, isAuto: false, isInit: false, body: null, expression: exprSet.Expression),
                AutoInitAccessorAccessorDeclarationNode autoInit => BuildInitAccessor(autoInit.AccessorModifier, name, autoInit, isAbstract, isAuto: true, body: null, expression: null),
                BlockInitAccessorAccessorDeclarationNode blockInit => BuildInitAccessor(blockInit.AccessorModifier, name, blockInit, isAbstract, isAuto: false, body: blockInit.Body, expression: null),
                ExpressionInitAccessorAccessorDeclarationNode exprInit => BuildInitAccessor(exprInit.AccessorModifier, name, exprInit, isAbstract, isAuto: false, body: null, expression: exprInit.Expression),
                _ => setter,
            };
        }

        var property = new Property(name, type, isStatic)
        {
            Getter = getter,
            Setter = setter,
            Initializer = !isAbstract && blockProp.Initializer is { } initExpr ? new Expression(initExpr) : null,
        };
        typeDef.PropertiesList.Add(property);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Validation
    // ═══════════════════════════════════════════════════════════════════════

    private PropertyAccessor BuildGetter(
        ModifierNode? modifier, string propertyName, Storm.Runtime.SyntaxNode locationNode,
        bool isAbstract, bool isAuto, Storm.Runtime.SyntaxNode? body, Storm.Runtime.SyntaxNode? expression)
    {
        if(modifier is not null)
        {
            if(isAbstract)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, propertyName);
            }
            else
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.GetterCannotHaveModifier, locationNode, null, propertyName);
            }
        }

        return new PropertyAccessor
        {
            IsAuto = isAuto,
            Body = body is not null ? new Block(body) : null,
            ExpressionBody = expression is not null ? new Expression(expression) : null,
        };
    }

    private PropertyAccessor? BuildSetter(
        ModifierNode? modifier, string propertyName, Storm.Runtime.SyntaxNode locationNode,
        bool isAbstract, bool isAuto, bool isInit, Storm.Runtime.SyntaxNode? body, Storm.Runtime.SyntaxNode? expression)
    {
        var isPrivate = false;

        if(modifier is not null)
        {
            if(isAbstract)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, propertyName);
            }
            else if(modifier is PrivateModifierModifierNode)
            {
                isPrivate = true;
            }
            else
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.SetterModifierMustBePrivate, locationNode, null, propertyName);
            }
        }

        return new PropertyAccessor
        {
            IsAuto = isAuto,
            IsInit = isInit,
            IsPrivate = isPrivate,
            Body = body is not null ? new Block(body) : null,
            ExpressionBody = expression is not null ? new Expression(expression) : null,
        };
    }

    private PropertyAccessor? BuildInitAccessor(
        ModifierNode? modifier, string propertyName, Storm.Runtime.SyntaxNode locationNode,
        bool isAbstract, bool isAuto, Storm.Runtime.SyntaxNode? body, Storm.Runtime.SyntaxNode? expression)
    {
        if(isAbstract)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AbstractPropertyInitAccessor, locationNode, null, propertyName);
            return null;
        }

        return BuildSetter(modifier, propertyName, locationNode, isAbstract: false, isAuto, isInit: true, body, expression);
    }
}