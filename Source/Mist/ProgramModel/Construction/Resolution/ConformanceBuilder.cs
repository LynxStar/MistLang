using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Phase 2c builder for retroactive conformance declarations (with blocks).
/// Processes all stashed <see cref="PendingConformance"/> entries after types
/// and extensions have been fully resolved.
/// </summary>
internal sealed class ConformanceBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;

    public ConformanceBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>
    /// Resolves all stashed with-declarations into <see cref="ConformanceDeclaration"/> instances.
    /// </summary>
    public void BuildAll()
    {
        foreach(var pending in context.PendingConformances)
        {
            Build(pending);
        }
    }

    private void Build(PendingConformance pending)
    {
        var decl = pending.Declaration;

        // Resolve the conforming type.
        var conformingType = scopeResolver.ResolveNamedType(decl.TypeName.Lexeme, pending.Scope, decl);

        // Resolve the abstract type, accounting for arity if type args are present.
        var abstractName = decl.AbstractName.Lexeme;
        IMistType abstractType;

        if(decl.AbstractTypeArgs is { } typeArgList)
        {
            var arity = 1 + typeArgList.TypeArguments.Length;
            var arityName = ScopeResolver.BuildArityName(abstractName, arity);

            var definition = scopeResolver.ResolveNamedType(arityName, pending.Scope, decl);
            var typeArgs = scopeResolver.ResolveTypeArguments(typeArgList, pending.Scope);
            context.ProgramModel.ConstructorConstraintClosures.RecordType(definition, typeArgs, decl);
            abstractType = context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArgs);
        }
        else
        {
            abstractType = scopeResolver.ResolveNamedType(abstractName, pending.Scope, decl);
        }

        // Collect member mappings.
        var mappings = new List<MemberMapping>();

        foreach(var mapping in decl.Mappings)
        {
            mappings.Add(new MemberMapping(mapping.SourceName.Lexeme, mapping.TargetName.Lexeme));
        }

        var conformance = new ConformanceDeclaration(conformingType, abstractType, pending.NamespaceName, mappings);
        context.ProgramModel.ConformancesList.Add(conformance);
    }
}