using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

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

        // Either end already failed to resolve and was reported as such. Naming it again as the wrong
        // KIND of type would describe a consequence of that failure as a second fault.
        if(conformingType is ErrorType || abstractType is ErrorType)
        {
            return;
        }

        if(conformingType is not ConcreteType conformer)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceSourceNotConcrete, decl, null,
                TypeDisplay.Mist(conformingType));
            return;
        }

        var target = abstractType is ConstructedType constructed ? constructed.Definition : abstractType;

        if(target is not AbstractType slots)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceTargetNotAbstract, decl, null,
                TypeDisplay.Mist(abstractType), TypeDisplay.Mist(conformingType));
            return;
        }

        var mappings = new List<MemberMapping>();

        foreach(var mapping in decl.Mappings)
        {
            if(Resolve(conformer, abstractType, slots, mapping, decl) is { } resolved)
            {
                mappings.Add(resolved);
            }
        }

        var conformance = new ConformanceDeclaration(
            conformer, abstractType, pending.NamespaceName, mappings, decl);

        // Recorded in two places on purpose, holding the one instance. The conforming type answers
        // "what does THIS type conform to", which is what assignability and emission ask starting
        // from a type; the flat list answers "what conformances exist", which is the only way to
        // reach one whose conforming type this backend never emits.
        conformer.ConformancesList.Add(conformance);
        context.ProgramModel.ConformancesList.Add(conformance);
    }

    private MemberMapping? Resolve(
        ConcreteType conformer,
        IMistType named,
        AbstractType definition,
        MemberMappingNode mapping,
        SyntaxNode node)
    {
        var targetName = mapping.TargetName.Lexeme;
        var sourceName = mapping.SourceName.Lexeme;

        if(FindSlot(named, definition, targetName) is not { } slot)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceUnresolvedSlot, node, null,
                TypeDisplay.Mist(named), targetName);
            return null;
        }

        // The conformer's own lookup walks its base CLASSES, which is what lets a with block map a
        // member the conforming type inherits rather than declares.
        var source = TypeMemberLookup.Find(
            conformer, LookupRequest.Member(sourceName, MemberVisibility.InstanceAndStatic, node));

        var resolved = Pair(slot, source);

        if(resolved is null)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ConformanceMappingUnsatisfied, node, null,
                sourceName, TypeDisplay.Mist(conformer), targetName, TypeDisplay.Mist(named));
        }

        return resolved;
    }

    /// <summary>
    /// A source member paired with the slot, on kind and then on parameter count. Parameter count is
    /// as far as the with-block syntax can reach: a mapping names two members and nothing else, so an
    /// abstract declaring one slot name at two arities gives no way to say which is meant, and that is
    /// refused rather than guessed at.
    /// </summary>
    private static MemberMapping? Pair(Slot slot, LookupResult source)
    {
        if(slot.Property is { } propertySlot)
        {
            return source is PropertyResult found
                ? new PropertyMapping(found.Property, propertySlot, slot.Declarer)
                : null;
        }

        if(slot.Methods.Count != 1 || source is not MethodGroupResult sources)
        {
            return null;
        }

        foreach(var candidate in sources.Methods)
        {
            if(candidate.ParametersList.Count == slot.Methods[0].ParametersList.Count)
            {
                return new MethodMapping(candidate, slot.Methods[0], slot.Declarer);
            }
        }

        return null;
    }

    /// <summary>
    /// The slot, and the abstract that declares it. Walked here rather than through
    /// <see cref="TypeMemberLookup"/> because that answers what a name MEANS on a type and this needs
    /// to know WHERE it was declared, which the method result does not carry.
    /// <para>
    /// Breadth-first with the nearest declaration winning, matching how
    /// <c>DefaultMemberDuplication</c> resolves a diamond, so the two agree about which of two
    /// abstracts owns a shared name.
    /// </para>
    /// </summary>
    private static Slot? FindSlot(IMistType named, AbstractType definition, string name)
    {
        var queue = new Queue<(IMistType Named, AbstractType Definition)>();
        var seen = new HashSet<AbstractType> { definition };

        queue.Enqueue((named, definition));

        while(queue.Count > 0)
        {
            var (currentNamed, current) = queue.Dequeue();

            var methods = current.MethodsList.Where(method => method.Name == name).ToList();

            if(methods.Count > 0)
            {
                return new Slot(currentNamed, methods, null);
            }

            if(current.PropertiesList.FirstOrDefault(property => property.Name == name) is { } found)
            {
                return new Slot(currentNamed, [], found);
            }

            foreach(var inherited in current.BaseAbstractTypes)
            {
                var inheritedDefinition =
                    (inherited is ConstructedType constructed ? constructed.Definition : inherited) as AbstractType;

                if(inheritedDefinition is not null && seen.Add(inheritedDefinition))
                {
                    queue.Enqueue((inherited, inheritedDefinition));
                }
            }
        }

        return null;
    }

    private sealed record Slot(IMistType Declarer, IReadOnlyList<Method> Methods, Property? Property);
}