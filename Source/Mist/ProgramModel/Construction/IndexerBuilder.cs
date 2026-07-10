using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves indexer declarations into <see cref="Indexer"/> instances.
/// A single code path handles both concrete and abstract contexts.
/// </summary>
internal sealed class IndexerBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly AccessorBuilder accessorBuilder;
    private readonly ParameterBuilder parameterBuilder;

    public IndexerBuilder(BuilderContext context, TypeReferenceResolver typeResolver, AccessorBuilder accessorBuilder, ParameterBuilder parameterBuilder)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        this.accessorBuilder = accessorBuilder;
        this.parameterBuilder = parameterBuilder;
    }

    /// <summary>
    /// Resolves an indexer declaration on any member-bearing type.
    /// </summary>
    public void Resolve(IndexerDeclarationNode indexerDecl, MemberType memberType)
    {
        var type = typeResolver.Resolve(indexerDecl.Type, memberType.Scope);
        var modifiers = ModifierBuilder.Build(indexerDecl, context.Diagnostics);
        var parameters = parameterBuilder.Resolve(indexerDecl.Parameters, memberType.Scope);
        var isAbstract = memberType is AbstractType;

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, indexerDecl, null);
        }

        if(modifiers.IsIntrinsic)
        {
            var typeIsIntrinsic = memberType is ConcreteType concrete && concrete.IsIntrinsic;
            IntrinsicMemberValidation.Validate(typeIsIntrinsic, memberType.Name, modifiers.IsPublic, indexerDecl, context.Diagnostics);
        }

        // Auto-accessors (get; / set;) are valid on indexers only in abstract
        // types or intrinsic indexers — not on regular concrete types.
        var allowAutoAccessors = isAbstract || modifiers.IsIntrinsic;

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
                    AutoGetAccessorAccessorDeclarationNode autoGet => BuildAutoGet(autoGet, isAbstract, allowAutoAccessors),
                    BlockGetAccessorAccessorDeclarationNode blockGet => BuildBlockGet(blockGet, isAbstract, modifiers.IsIntrinsic),
                    ExpressionGetAccessorAccessorDeclarationNode exprGet => BuildExpressionGet(exprGet, isAbstract, modifiers.IsIntrinsic),
                    _ => getter,
                };

                setter = accessor switch
                {
                    AutoSetAccessorAccessorDeclarationNode autoSet => BuildAutoSet(autoSet, isAbstract, allowAutoAccessors),
                    BlockSetAccessorAccessorDeclarationNode blockSet => BuildBlockSet(blockSet, isAbstract, modifiers.IsIntrinsic),
                    ExpressionSetAccessorAccessorDeclarationNode exprSet => BuildExpressionSet(exprSet, isAbstract, modifiers.IsIntrinsic),
                    AutoInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    BlockInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    ExpressionInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    _ => setter,
                };
            }
        }

        var indexer = new Indexer(type, parameters, modifiers) { Getter = getter, Setter = setter };

        if(getter is null && setter is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.EmptyAccessorList, indexerDecl, null, "indexer");
        }

        memberType.IndexersList.Add(indexer);
    }

    private PropertyAccessor BuildAutoGet(AutoGetAccessorAccessorDeclarationNode autoGet, bool isAbstract, bool allowAutoAccessors)
    {
        if(!allowAutoAccessors)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IndexerCannotHaveAutoAccessor, autoGet, null);
        }

        return accessorBuilder.BuildGetter(autoGet.AccessorModifier, "indexer", autoGet, isAbstract, isAuto: true, body: null, expression: null);
    }

    private PropertyAccessor BuildAutoSet(AutoSetAccessorAccessorDeclarationNode autoSet, bool isAbstract, bool allowAutoAccessors)
    {
        if(!allowAutoAccessors)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IndexerCannotHaveAutoAccessor, autoSet, null);
        }

        return accessorBuilder.BuildSetter(autoSet.AccessorModifier, "indexer", autoSet, isAbstract, isAuto: true, isInit: false, body: null, expression: null);
    }

    private PropertyAccessor BuildBlockGet(BlockGetAccessorAccessorDeclarationNode blockGet, bool isAbstract, bool isIntrinsic)
    {
        if(isIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, blockGet, null, "indexer");
        }

        return accessorBuilder.BuildGetter(blockGet.AccessorModifier, "indexer", blockGet, isAbstract, isAuto: false, body: blockGet.Body, expression: null);
    }

    private PropertyAccessor BuildExpressionGet(ExpressionGetAccessorAccessorDeclarationNode exprGet, bool isAbstract, bool isIntrinsic)
    {
        if(isIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, exprGet, null, "indexer");
        }

        return accessorBuilder.BuildGetter(exprGet.AccessorModifier, "indexer", exprGet, isAbstract, isAuto: false, body: null, expression: exprGet.Expression);
    }

    private PropertyAccessor BuildBlockSet(BlockSetAccessorAccessorDeclarationNode blockSet, bool isAbstract, bool isIntrinsic)
    {
        if(isIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, blockSet, null, "indexer");
        }

        return accessorBuilder.BuildSetter(blockSet.AccessorModifier, "indexer", blockSet, isAbstract, isAuto: false, isInit: false, body: blockSet.Body, expression: null);
    }

    private PropertyAccessor BuildExpressionSet(ExpressionSetAccessorAccessorDeclarationNode exprSet, bool isAbstract, bool isIntrinsic)
    {
        if(isIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, exprSet, null, "indexer");
        }

        return accessorBuilder.BuildSetter(exprSet.AccessorModifier, "indexer", exprSet, isAbstract, isAuto: false, isInit: false, body: null, expression: exprSet.Expression);
    }

    private PropertyAccessor? RejectInitAccessor(Storm.Runtime.SyntaxNode locationNode)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IndexerCannotHaveInit, locationNode, null);
        return null;
    }
}