using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.StorageWrites;

/// <summary>
/// For every member whose body a type parameter is in scope of, the parameters whose storage reaching
/// it may write, per direction (unknown-type Requirement 9). Computed once, on the first receiver closed
/// with <c>unknown</c> that asks, from the bodies of every such member -- those not yet bound by then
/// are bound in a discarded trial, so the answer depends on the program and not on which declaration
/// the binder happened to reach first. Bodiless members answer from their signatures: a value flows in
/// through a parameter, so a parameter's type is what such a member may store.
/// </summary>
internal sealed class StorageWriteSummaries
{
    private static readonly IReadOnlySet<TypeParameter> None = new HashSet<TypeParameter>();

    private readonly HashSet<object> complete = new(ReferenceEqualityComparer.Instance);

    private Dictionary<(object Member, StorageAccess Side), HashSet<TypeParameter>>? computed;

    private bool computing;

    /// <summary>
    /// Records that <paramref name="member"/>'s bodies now hold their final trees, so the trial can
    /// read them instead of binding again.
    /// </summary>
    public void MarkComplete(object member)
    {
        _ = complete.Add(member);
    }

    public bool IsComplete(object member)
    {
        return complete.Contains(member);
    }

    /// <summary>
    /// False while the summaries are being computed, when the trial binding is in progress and every
    /// site admits what it finds; the real binding of those bodies asks again and is answered.
    /// </summary>
    public bool TryWrites(object member, StorageAccess side, BuilderContext context, out IReadOnlySet<TypeParameter> writes)
    {
        if(computing)
        {
            writes = None;
            return false;
        }

        computed ??= Compute(context);
        var canonical = Canonical(member);
        var direction = member is Method || !side.HasFlag(StorageAccess.Write) ? StorageAccess.Read : StorageAccess.Write;

        writes = computed.TryGetValue((canonical, direction), out var summarized) ? summarized : FromSignature(canonical, direction);
        return true;
    }

    public static object Canonical(object member)
    {
        return member is Method { SourceDeclaration: { } source } ? source : member;
    }

    /// <summary>
    /// What a member with no body to read may write: every parameter its inputs are typed by. An
    /// <c>out</c> parameter carries nothing in, so it is left out; a setter's value is an input of its
    /// own.
    /// </summary>
    public static HashSet<TypeParameter> FromSignature(object member, StorageAccess side)
    {
        var writes = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);

        switch(member)
        {
            case Method method:
                CollectParameters(method.ParametersList, writes);
                break;

            case Field field when side.HasFlag(StorageAccess.Write):
                TypeParameterOccurrences.Collect(field.Type, writes);
                break;

            case Property property when side.HasFlag(StorageAccess.Write):
                TypeParameterOccurrences.Collect(property.Type, writes);
                break;

            case Indexer indexer:
                CollectParameters(indexer.ParametersList, writes);

                if(side.HasFlag(StorageAccess.Write))
                {
                    TypeParameterOccurrences.Collect(indexer.Type, writes);
                }

                break;
        }

        return writes;
    }

    private static void CollectParameters(IReadOnlyList<Parameter> parameters, HashSet<TypeParameter> into)
    {
        foreach(var parameter in parameters)
        {
            if(parameter.Modifier.Direction != ByRefDirection.Out)
            {
                TypeParameterOccurrences.Collect(parameter.Type, into);
            }
        }
    }

    private Dictionary<(object, StorageAccess), HashSet<TypeParameter>> Compute(BuilderContext context)
    {
        computing = true;

        try
        {
            return Solve(StorageWriteRoots.Gather(context, complete), context);
        }
        finally
        {
            computing = false;
        }
    }

    internal static Dictionary<(object, StorageAccess), HashSet<TypeParameter>> Solve(IReadOnlyList<StorageWriteRoot> roots, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;
        var effects = new Dictionary<(object, StorageAccess), StorageEffect>();
        var scopes = new Dictionary<(object, StorageAccess), HashSet<TypeParameter>>();
        var reader = new StorageEffects(context.ProgramModel);

        foreach(var root in roots)
        {
            var key = (Canonical(root.Member), root.Side);

            if(!effects.TryGetValue(key, out var effect))
            {
                effect = new StorageEffect();
                effects[key] = effect;
                scopes[key] = root.Scope;

                if(key.Item1 is Constructor constructor)
                {
                    effect.Direct.UnionWith(FromSignature(constructor, StorageAccess.Read));

                    if(constructor.ChainedConstructor is { DeclaringType: { } chained } chainedConstructor && root.Owner is { } owner)
                    {
                        effect.Edges.Add(new StorageEdge(
                            chainedConstructor, StorageAccess.Read, Ancestry.SubstitutionTo(owner, chained, interner) ?? TypeSubstitution.Empty));
                    }
                }
            }

            if(root.Root is { } body)
            {
                reader.Collect(body, root.Owner, effect);
            }
        }

        var writes = effects.ToDictionary(
            pair => pair.Key,
            pair => new HashSet<TypeParameter>(pair.Value.Direct.Where(scopes[pair.Key].Contains), ReferenceEqualityComparer.Instance));

        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var (key, effect) in effects)
            {
                var own = writes[key];
                var scope = scopes[key];

                foreach(var edge in effect.Edges)
                {
                    var calleeKey = (Canonical(edge.Callee), edge.Side);
                    var calleeWrites = writes.TryGetValue(calleeKey, out var summarized) ? summarized : FromSignature(calleeKey.Item1, edge.Side);

                    foreach(var written in calleeWrites)
                    {
                        foreach(var parameter in TypeParameterOccurrences.In(edge.Substitution.Apply(written, interner)))
                        {
                            changed |= scope.Contains(parameter) && own.Add(parameter);
                        }
                    }
                }
            }
        }

        return writes;
    }
}