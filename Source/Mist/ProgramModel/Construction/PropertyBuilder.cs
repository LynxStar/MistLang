using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves property declarations into <see cref="Property"/> instances.
/// A single code path handles both concrete and abstract contexts.
/// </summary>
internal sealed class PropertyBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly AccessorBuilder accessorBuilder;

    public PropertyBuilder(BuilderContext context, TypeReferenceResolver typeResolver, AccessorBuilder accessorBuilder)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        this.accessorBuilder = accessorBuilder;
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
        var modifiers = ModifierBuilder.Build(exprProp.Modifiers, context.Diagnostics, exprProp);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, exprProp, null);
        }

        var getter = new PropertyAccessor { ExpressionBody = new Expression(exprProp.Body) };
        var property = new Property(name, type, modifiers) { Getter = getter };
        typeDef.PropertiesList.Add(property);
    }

    private void ResolveBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, MemberType typeDef, bool isAbstract)
    {
        var name = blockProp.Name.Lexeme;
        var type = typeResolver.Resolve(blockProp.Type, typeDef.Scope);
        var modifiers = ModifierBuilder.Build(blockProp.Modifiers, context.Diagnostics, blockProp);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, blockProp, null);
        }

        PropertyAccessor? getter = null;
        PropertyAccessor? setter = null;

        foreach(var accessor in blockProp.Accessors)
        {
            getter = accessor switch
            {
                AutoGetAccessorAccessorDeclarationNode autoGet => accessorBuilder.BuildGetter(autoGet.AccessorModifier, name, autoGet, isAbstract, isAuto: true, body: null, expression: null),
                BlockGetAccessorAccessorDeclarationNode blockGet => accessorBuilder.BuildGetter(blockGet.AccessorModifier, name, blockGet, isAbstract, isAuto: false, body: blockGet.Body, expression: null),
                ExpressionGetAccessorAccessorDeclarationNode exprGet => accessorBuilder.BuildGetter(exprGet.AccessorModifier, name, exprGet, isAbstract, isAuto: false, body: null, expression: exprGet.Expression),
                _ => getter,
            };

            setter = accessor switch
            {
                AutoSetAccessorAccessorDeclarationNode autoSet => accessorBuilder.BuildSetter(autoSet.AccessorModifier, name, autoSet, isAbstract, isAuto: true, isInit: false, body: null, expression: null),
                BlockSetAccessorAccessorDeclarationNode blockSet => accessorBuilder.BuildSetter(blockSet.AccessorModifier, name, blockSet, isAbstract, isAuto: false, isInit: false, body: blockSet.Body, expression: null),
                ExpressionSetAccessorAccessorDeclarationNode exprSet => accessorBuilder.BuildSetter(exprSet.AccessorModifier, name, exprSet, isAbstract, isAuto: false, isInit: false, body: null, expression: exprSet.Expression),
                AutoInitAccessorAccessorDeclarationNode autoInit => accessorBuilder.BuildInitAccessor(autoInit.AccessorModifier, name, autoInit, isAbstract, isAuto: true, body: null, expression: null),
                BlockInitAccessorAccessorDeclarationNode blockInit => accessorBuilder.BuildInitAccessor(blockInit.AccessorModifier, name, blockInit, isAbstract, isAuto: false, body: blockInit.Body, expression: null),
                ExpressionInitAccessorAccessorDeclarationNode exprInit => accessorBuilder.BuildInitAccessor(exprInit.AccessorModifier, name, exprInit, isAbstract, isAuto: false, body: null, expression: exprInit.Expression),
                _ => setter,
            };
        }

        var property = new Property(name, type, modifiers)
        {
            Getter = getter,
            Setter = setter,
            Initializer = !isAbstract && blockProp.Initializer is { } initExpr ? new Expression(initExpr) : null,
        };

        if(getter is null && setter is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.EmptyAccessorList, blockProp, null, name);
        }

        typeDef.PropertiesList.Add(property);
    }
}