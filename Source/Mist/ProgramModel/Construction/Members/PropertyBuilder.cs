using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves property declarations into <see cref="Property"/> instances.
/// A single code path handles both concrete and abstract contexts.
/// </summary>
internal sealed class PropertyBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;
    private readonly AccessorBuilder accessorBuilder;

    public PropertyBuilder(BuilderContext context, ScopeResolver scopeResolver, AccessorBuilder accessorBuilder)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
        this.accessorBuilder = accessorBuilder;
    }

    /// <summary>
    /// Resolves a property declaration on any member-bearing type.
    /// </summary>
    public void Resolve(PropertyDeclarationNode propertyDecl, IMethodHost host)
    {
        switch(propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                ResolveBlockProperty(blockProp, host);
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                ResolveExpressionBodiedProperty(exprProp, host);
                break;
        }
    }

    private void ResolveExpressionBodiedProperty(ExpressionBodiedPropertyPropertyDeclarationNode exprProp, IMethodHost host)
    {
        var name = exprProp.Name.Lexeme;
        var type = scopeResolver.Resolve(exprProp.Type, host.Scope);
        var modifiers = ModifierBuilder.ForMember(exprProp, context.Diagnostics, host as MistType);

        MemberNameValidation.Validate(name, host, exprProp, context.Diagnostics);
        StorageTypeValidation.Validate(type, $"Property '{host.Name}.{name}'", exprProp, context);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, exprProp, null);
        }

        if(modifiers.IsIntrinsic)
        {
            IntrinsicMemberValidation.Validate(host.IsIntrinsic, host.Name, modifiers.Accessibility == Accessibility.Public, exprProp, context.Diagnostics);

            // An expression-bodied property supplies a getter body, so it can never
            // be a valid intrinsic (backend-provided) property.
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, exprProp, null, name);
        }

        var getter = new PropertyAccessor { ExpressionBody = new UnresolvedExpression(exprProp.Body) };
        var property = new Property(name, SpreadMember.AggregateType(type, scopeResolver.Interner), modifiers)
        {
            DeclaringType = host as MemberType,
            DeclarationNode = exprProp,
            Getter = getter,
        };
        context.Attributes.Build(exprProp.Attributes, property.DeclaredAttributesList, host.Scope);
        host.PropertiesList.Add(property);
    }

    private void ResolveBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, IMethodHost host)
    {
        var name = blockProp.Name.Lexeme;
        var type = scopeResolver.Resolve(blockProp.Type, host.Scope);
        var modifiers = ModifierBuilder.ForMember(blockProp, context.Diagnostics, host as MistType);
        var isAbstract = host.IsAbstract;
        var isExtension = host is ExtensionBlock;

        MemberNameValidation.Validate(name, host, blockProp, context.Diagnostics);
        StorageTypeValidation.Validate(type, $"Property '{host.Name}.{name}'", blockProp, context);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, blockProp, null);
        }

        if(modifiers.IsIntrinsic)
        {
            IntrinsicMemberValidation.Validate(host.IsIntrinsic, host.Name, modifiers.Accessibility == Accessibility.Public, blockProp, context.Diagnostics);
        }

        var site = new AccessorSite(name, modifiers, host as MemberType, isAbstract);
        PropertyAccessor? getter = null;
        PropertyAccessor? setter = null;

        foreach(var accessor in blockProp.Accessors)
        {
            if(isExtension && accessor is AutoGetAccessorAccessorDeclarationNode
                or AutoSetAccessorAccessorDeclarationNode
                or AutoInitAccessorAccessorDeclarationNode)
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.ExtensionPropertyCannotHaveAutoAccessor, accessor, null, name);
            }

            // An intrinsic property is backend-provided: every accessor must be an
            // auto accessor (get; / set; / init;). A block or expression body means
            // the developer supplied an implementation, which is not allowed.
            if(modifiers.IsIntrinsic && accessor is not (
                AutoGetAccessorAccessorDeclarationNode or
                AutoSetAccessorAccessorDeclarationNode or
                AutoInitAccessorAccessorDeclarationNode))
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, accessor, null, name);
            }

            getter = accessor switch
            {
                AutoGetAccessorAccessorDeclarationNode autoGet => accessorBuilder.BuildGetter(autoGet.Promise, autoGet.AccessorModifier, site, autoGet, isAuto: true, body: null, expression: null),
                BlockGetAccessorAccessorDeclarationNode blockGet => accessorBuilder.BuildGetter(blockGet.Promise, blockGet.AccessorModifier, site, blockGet, isAuto: false, body: blockGet.Body, expression: null),
                ExpressionGetAccessorAccessorDeclarationNode exprGet => accessorBuilder.BuildGetter(exprGet.Promise, exprGet.AccessorModifier, site, exprGet, isAuto: false, body: null, expression: exprGet.Expression),
                _ => getter,
            };

            setter = accessor switch
            {
                AutoSetAccessorAccessorDeclarationNode autoSet => accessorBuilder.BuildSetter(autoSet.Promise, autoSet.AccessorModifier, site, autoSet, isAuto: true, isInit: false, body: null, expression: null),
                BlockSetAccessorAccessorDeclarationNode blockSet => accessorBuilder.BuildSetter(blockSet.Promise, blockSet.AccessorModifier, site, blockSet, isAuto: false, isInit: false, body: blockSet.Body, expression: null),
                ExpressionSetAccessorAccessorDeclarationNode exprSet => accessorBuilder.BuildSetter(exprSet.Promise, exprSet.AccessorModifier, site, exprSet, isAuto: false, isInit: false, body: null, expression: exprSet.Expression),
                AutoInitAccessorAccessorDeclarationNode autoInit => accessorBuilder.BuildInitAccessor(autoInit.Promise, autoInit.AccessorModifier, site, autoInit, isAuto: true, body: null, expression: null),
                BlockInitAccessorAccessorDeclarationNode blockInit => accessorBuilder.BuildInitAccessor(blockInit.Promise, blockInit.AccessorModifier, site, blockInit, isAuto: false, body: blockInit.Body, expression: null),
                ExpressionInitAccessorAccessorDeclarationNode exprInit => accessorBuilder.BuildInitAccessor(exprInit.Promise, exprInit.AccessorModifier, site, exprInit, isAuto: false, body: null, expression: exprInit.Expression),
                _ => setter,
            };
        }

        var property = new Property(name, SpreadMember.AggregateType(type, scopeResolver.Interner), modifiers)
        {
            DeclaringType = host as MemberType,
            DeclarationNode = blockProp,
            Getter = getter,
            Setter = setter,
            Initializer = !isAbstract && blockProp.Initializer is { } initExpr ? new UnresolvedExpression(initExpr) : null,
        };

        if(getter is null && setter is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.EmptyAccessorList, blockProp, null, name);
        }

        context.Attributes.Build(blockProp.Attributes, property.DeclaredAttributesList, host.Scope);
        host.PropertiesList.Add(property);
    }
}