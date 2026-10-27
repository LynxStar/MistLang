using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves indexer declarations into <see cref="Indexer"/> instances.
/// A single code path handles both concrete and abstract contexts.
/// </summary>
internal sealed class IndexerBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;
    private readonly AccessorBuilder accessorBuilder;
    private readonly ParameterBuilder parameterBuilder;
    private readonly ConstraintResolver constraintResolver;

    public IndexerBuilder(
        BuilderContext context, ScopeResolver scopeResolver, AccessorBuilder accessorBuilder, ParameterBuilder parameterBuilder,
        ConstraintResolver constraintResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
        this.accessorBuilder = accessorBuilder;
        this.parameterBuilder = parameterBuilder;
        this.constraintResolver = constraintResolver;
    }

    /// <summary>
    /// Resolves an indexer declaration on any member-bearing type.
    /// </summary>
    public void Resolve(IndexerDeclarationNode indexerDecl, MemberType memberType)
    {
        var type = scopeResolver.Resolve(indexerDecl.Type, memberType.Scope);
        var modifiers = ModifierBuilder.ForMember(indexerDecl, context.Diagnostics, memberType);
        var parameters = parameterBuilder.Resolve(indexerDecl.Parameters, memberType.Scope);
        constraintResolver.Resolve(indexerDecl.Constraints, [], parameters, memberType.Scope);
        var isAbstract = memberType is AbstractType;

        StorageTypeValidation.Validate(type, $"Indexer '{memberType.Name}.this[]'", indexerDecl, context);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AsyncOnlyOnMethods, indexerDecl, null);
        }

        if(modifiers.IsIntrinsic)
        {
            var typeIsIntrinsic = memberType is ConcreteType concrete && concrete.IsIntrinsic;
            IntrinsicMemberValidation.Validate(typeIsIntrinsic, memberType.Name, modifiers.Accessibility == Accessibility.Public, indexerDecl, context.Diagnostics);
        }

        // Auto-accessors (get; / set;) are valid on indexers only in abstract
        // types or intrinsic indexers — not on regular concrete types.
        var allowAutoAccessors = isAbstract || modifiers.IsIntrinsic;

        var site = new AccessorSite("indexer", modifiers, memberType, isAbstract);
        PropertyAccessor? getter = null;
        PropertyAccessor? setter = null;

        var accessors = indexerDecl.Body switch
        {
            BlockIndexerBodyIndexerBodyNode blockBody => blockBody.Accessors,
            _ => default,
        };

        if(!accessors.IsDefault)
        {
            foreach(var accessor in accessors)
            {
                getter = accessor switch
                {
                    AutoGetAccessorAccessorDeclarationNode autoGet => BuildAutoGet(autoGet, site, allowAutoAccessors),
                    BlockGetAccessorAccessorDeclarationNode blockGet => BuildBlockGet(blockGet, site),
                    ExpressionGetAccessorAccessorDeclarationNode exprGet => BuildExpressionGet(exprGet, site),
                    _ => getter,
                };

                setter = accessor switch
                {
                    AutoSetAccessorAccessorDeclarationNode autoSet => BuildAutoSet(autoSet, site, allowAutoAccessors),
                    BlockSetAccessorAccessorDeclarationNode blockSet => BuildBlockSet(blockSet, site),
                    ExpressionSetAccessorAccessorDeclarationNode exprSet => BuildExpressionSet(exprSet, site),
                    AutoInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    BlockInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    ExpressionInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    _ => setter,
                };
            }
        }

        var indexer = new Indexer(type, parameters, modifiers)
        {
            DeclaringType = memberType,
            DeclarationNode = indexerDecl,
            Getter = getter,
            Setter = setter,
        };

        if(getter is null && setter is null)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.EmptyAccessorList, indexerDecl, null, new NameValue("indexer"));
        }

        context.Attributes.Build(indexerDecl.Attributes, indexer.DeclaredAttributesList, memberType.Scope);
        memberType.IndexersList.Add(indexer);
    }

    private PropertyAccessor BuildAutoGet(AutoGetAccessorAccessorDeclarationNode autoGet, AccessorSite site, bool allowAutoAccessors)
    {
        if(!allowAutoAccessors)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IndexerCannotHaveAutoAccessor, autoGet, null);
        }

        return accessorBuilder.BuildGetter(autoGet.Promise, autoGet.AccessorModifier, site, autoGet, isAuto: true, body: null, expression: null);
    }

    private PropertyAccessor BuildAutoSet(AutoSetAccessorAccessorDeclarationNode autoSet, AccessorSite site, bool allowAutoAccessors)
    {
        if(!allowAutoAccessors)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IndexerCannotHaveAutoAccessor, autoSet, null);
        }

        return accessorBuilder.BuildSetter(autoSet.Promise, autoSet.AccessorModifier, site, autoSet, isAuto: true, isInit: false, body: null, expression: null);
    }

    private PropertyAccessor BuildBlockGet(BlockGetAccessorAccessorDeclarationNode blockGet, AccessorSite site)
    {
        RefuseBodyOnIntrinsic(blockGet, site);

        return accessorBuilder.BuildGetter(blockGet.Promise, blockGet.AccessorModifier, site, blockGet, isAuto: false, body: blockGet.Body, expression: null);
    }

    private PropertyAccessor BuildExpressionGet(ExpressionGetAccessorAccessorDeclarationNode exprGet, AccessorSite site)
    {
        RefuseBodyOnIntrinsic(exprGet, site);

        return accessorBuilder.BuildGetter(exprGet.Promise, exprGet.AccessorModifier, site, exprGet, isAuto: false, body: null, expression: exprGet.Expression);
    }

    private PropertyAccessor BuildBlockSet(BlockSetAccessorAccessorDeclarationNode blockSet, AccessorSite site)
    {
        RefuseBodyOnIntrinsic(blockSet, site);

        return accessorBuilder.BuildSetter(blockSet.Promise, blockSet.AccessorModifier, site, blockSet, isAuto: false, isInit: false, body: blockSet.Body, expression: null);
    }

    private PropertyAccessor BuildExpressionSet(ExpressionSetAccessorAccessorDeclarationNode exprSet, AccessorSite site)
    {
        RefuseBodyOnIntrinsic(exprSet, site);

        return accessorBuilder.BuildSetter(exprSet.Promise, exprSet.AccessorModifier, site, exprSet, isAuto: false, isInit: false, body: null, expression: exprSet.Expression);
    }

    private void RefuseBodyOnIntrinsic(Storm.Runtime.SyntaxNode accessor, AccessorSite site)
    {
        if(site.Owner.IsIntrinsic)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, accessor, null, new NameValue("indexer"));
        }
    }

    private PropertyAccessor? RejectInitAccessor(Storm.Runtime.SyntaxNode locationNode)
    {
        context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IndexerCannotHaveInit, locationNode, null);
        return null;
    }
}