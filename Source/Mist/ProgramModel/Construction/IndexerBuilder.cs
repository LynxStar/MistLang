using Mist.Common.Collections;
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
    public void Resolve(IndexerDeclarationNode indexerDecl, MemberType typeDef, bool isAbstract)
    {
        var type = typeResolver.Resolve(indexerDecl.Type, typeDef.Scope);
        var isStatic = indexerDecl.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();
        var parameters = parameterBuilder.Resolve(indexerDecl.Parameters, typeDef.Scope);

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
                    AutoGetAccessorAccessorDeclarationNode autoGet => accessorBuilder.BuildGetter(autoGet.AccessorModifier, "indexer", autoGet, isAbstract, isAuto: true, body: null, expression: null),
                    BlockGetAccessorAccessorDeclarationNode blockGet => accessorBuilder.BuildGetter(blockGet.AccessorModifier, "indexer", blockGet, isAbstract, isAuto: false, body: blockGet.Body, expression: null),
                    ExpressionGetAccessorAccessorDeclarationNode exprGet => accessorBuilder.BuildGetter(exprGet.AccessorModifier, "indexer", exprGet, isAbstract, isAuto: false, body: null, expression: exprGet.Expression),
                    _ => getter,
                };

                setter = accessor switch
                {
                    AutoSetAccessorAccessorDeclarationNode autoSet => accessorBuilder.BuildSetter(autoSet.AccessorModifier, "indexer", autoSet, isAbstract, isAuto: true, isInit: false, body: null, expression: null),
                    BlockSetAccessorAccessorDeclarationNode blockSet => accessorBuilder.BuildSetter(blockSet.AccessorModifier, "indexer", blockSet, isAbstract, isAuto: false, isInit: false, body: blockSet.Body, expression: null),
                    ExpressionSetAccessorAccessorDeclarationNode exprSet => accessorBuilder.BuildSetter(exprSet.AccessorModifier, "indexer", exprSet, isAbstract, isAuto: false, isInit: false, body: null, expression: exprSet.Expression),
                    AutoInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    BlockInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    ExpressionInitAccessorAccessorDeclarationNode => RejectInitAccessor(indexerDecl),
                    _ => setter,
                };
            }
        }

        var indexer = new Indexer(type, parameters, isStatic) { Getter = getter, Setter = setter };

        if(getter is null && setter is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.EmptyAccessorList, indexerDecl, null, "indexer");
        }

        typeDef.IndexersList.Add(indexer);
    }

    private PropertyAccessor? RejectInitAccessor(Storm.Runtime.SyntaxNode locationNode)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IndexerCannotHaveInit, locationNode, null);
        return null;
    }
}