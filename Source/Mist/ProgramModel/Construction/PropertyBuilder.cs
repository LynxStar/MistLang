using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
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
    private readonly SpreadMemberBuilder spreadMemberBuilder;

    public PropertyBuilder(
        BuilderContext context,
        TypeReferenceResolver typeResolver,
        AccessorBuilder accessorBuilder,
        SpreadMemberBuilder spreadMemberBuilder)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        this.accessorBuilder = accessorBuilder;
        this.spreadMemberBuilder = spreadMemberBuilder;
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
        var modifiers = ModifierBuilder.Build(exprProp, context.Diagnostics, host as MistType);
        var type = ResolvePropertyType(exprProp.Type, name, host, modifiers, exprProp);
        if(type is null)
        {
            // Malformed spread member (empty stem or non-pack operand); already diagnosed.
            return;
        }

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

        var getter = new PropertyAccessor { ExpressionBody = new Expression(exprProp.Body) };
        var property = new Property(name, type, modifiers) { Getter = getter };
        host.PropertiesList.Add(property);
    }

    private void ResolveBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, IMethodHost host)
    {
        var name = blockProp.Name.Lexeme;
        var modifiers = ModifierBuilder.Build(blockProp, context.Diagnostics, host as MistType);
        var type = ResolvePropertyType(blockProp.Type, name, host, modifiers, blockProp);
        if(type is null)
        {
            // Malformed spread member (empty stem or non-pack operand); already diagnosed.
            return;
        }

        var isAbstract = host.IsAbstract;
        var isExtension = host is ExtensionBlock;

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, blockProp, null);
        }

        if(modifiers.IsIntrinsic)
        {
            IntrinsicMemberValidation.Validate(host.IsIntrinsic, host.Name, modifiers.Accessibility == Accessibility.Public, blockProp, context.Diagnostics);
        }

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

        host.PropertiesList.Add(property);
    }

    /// <summary>
    /// Resolves a property's declared type, handling the spread-generated member case.
    /// When the declared type is a pack spread (<c>public TArgs... Slot { get; }</c>), this
    /// resolves the aggregate value-pack tuple <c>(TArgs...)</c> and records the
    /// spread-generated member family on the host, so the aggregate <c>Slot</c> is built as
    /// an ordinary property here while its element projections <c>Slot1 .. SlotN</c> are
    /// materialized when the pack binds. Returns <c>null</c> when the spread is malformed —
    /// an empty stem (<c>MIST_VAR_0008</c>) or a non-pack operand, both already diagnosed —
    /// so the caller skips the member. A non-spread type resolves as usual (an unresolved
    /// type yields <see cref="ErrorType"/>, which is non-null, preserving normal handling).
    /// Spread members only surface on member-bearing types, so an extension-block host
    /// falls through to ordinary resolution.
    /// </summary>
    private IMistType? ResolvePropertyType(
        TypeReferenceNode typeRef,
        string stem,
        IMethodHost host,
        MemberModifiers modifiers,
        Storm.Runtime.SyntaxNode declarationNode)
    {
        if(host is MemberType memberHost && SpreadMemberBuilder.TryGetMemberSpread(typeRef, out var spread))
        {
            var aggregate = spreadMemberBuilder.ResolveAggregateAndRecord(
                spread, stem, memberHost, SpreadMemberKind.Property, modifiers, OwnershipKind.Unrestricted, declarationNode);
            return aggregate is ErrorType ? null : aggregate;
        }

        return typeResolver.Resolve(typeRef, host.Scope);
    }
}