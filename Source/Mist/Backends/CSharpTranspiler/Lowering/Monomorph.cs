using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// One variadic family specialized to one width per pack it declares: the C# declaration a program
/// that used the family that way needs, since C# has no pack to declare it with once.
/// <para>
/// AMBIENT FOR THE DURATION OF EMITTING IT, which is a deliberate choice about where this belongs.
/// The alternative is a substitution parameter threaded through every emitter that reads a type,
/// and there are twelve of them; a concern spread that thinly is one that gets forgotten in the
/// thirteenth. It is scoped to a single declaration, set and cleared in one place, and thread-static
/// so parallel emissions cannot see each other's.
/// </para>
/// </summary>
internal sealed class Monomorph
{
    [ThreadStatic]
    private static Monomorph? inScope;

    private readonly IReadOnlyList<IReadOnlyList<TypeParameter>> expansions;

    private Monomorph(IReadOnlyList<TypeParameter> packs, IReadOnlyList<IReadOnlyList<TypeParameter>> expansions)
    {
        Packs = packs;
        this.expansions = expansions;
    }

    /// <summary>
    /// The specialization being emitted, if any. A method rather than a property because the field
    /// behind it is thread-static, which an auto-property cannot be.
    /// </summary>
    public static Monomorph? InScope()
    {
        return inScope;
    }

    /// <summary>
    /// The packs this specialization fixes, in the order the declaration lists them. A declaration
    /// may carry more than one: only the ABSORBING spelling is limited to one, a flat argument list
    /// having no way to say where the first pack ends.
    /// </summary>
    public IReadOnlyList<TypeParameter> Packs { get; }

    public IReadOnlyList<int> Widths => [.. expansions.Select(expansion => expansion.Count)];

    /// <summary>
    /// The C# type parameters a pack became, one per element. Empty for an arity-zero
    /// specialization, which is a real one -- a family used with no arguments needs a declaration
    /// as much as any other -- and empty for a pack this specialization does not fix.
    /// </summary>
    public IReadOnlyList<TypeParameter> Expansion(TypeParameter pack)
    {
        var index = IndexOf(pack);
        return index < 0 ? [] : expansions[index];
    }

    public bool Binds(TypeParameter pack)
    {
        return IndexOf(pack) >= 0;
    }

    /// <summary>
    /// The packs a definition declares, empty when it declares none and needs no specialization.
    /// </summary>
    public static IReadOnlyList<TypeParameter> PacksOf(MistType definition)
    {
        return definition is IHasTypeParameters generic ? PacksIn(generic.TypeParametersList) : [];
    }

    /// <summary>
    /// The packs a declaration -- a type or a method -- declares, in slot order.
    /// </summary>
    public static IReadOnlyList<TypeParameter> PacksIn(IReadOnlyList<TypeParameter> parameters)
    {
        return [.. parameters.Where(parameter => parameter.PackKind != TypePackKind.None)];
    }

    /// <summary>
    /// Whether this specialization is the one a declaration carrying these parameters is being
    /// emitted under. Asked of the PACKS rather than of an owner, so a type and a method specialize
    /// through the same mechanism.
    /// </summary>
    public bool Owns(IReadOnlyList<TypeParameter> parameters)
    {
        return Packs.Any(pack => parameters.Any(parameter => ReferenceEquals(parameter, pack)));
    }

    public static Monomorph? For(IReadOnlyList<TypeParameter> parameters, IReadOnlyList<int> widths)
    {
        var packs = PacksIn(parameters);

        if(packs.Count == 0 || packs.Count != widths.Count)
        {
            return null;
        }

        var expansions = new List<IReadOnlyList<TypeParameter>>(packs.Count);
        var ordinal = 0;

        for(var i = 0; i < packs.Count; i++)
        {
            var expansion = new List<TypeParameter>(widths[i]);

            for(var element = 0; element < widths[i]; element++)
            {
                var parameter = new TypeParameter(
                    $"T{++ordinal}", ordinal - 1, new Scope(null), declaringType: null);

                // A constraint on the pack is a constraint on every element -- what makes a
                // constrained member callable on each of them -- and C# has only per-parameter
                // constraints to say that with, so the one clause becomes N.
                parameter.ConstraintsList.AddRange(packs[i].ConstraintsList);
                expansion.Add(parameter);
            }

            expansions.Add(expansion);
        }

        return new Monomorph(packs, expansions);
    }

    public IDisposable Enter()
    {
        var restored = inScope;
        inScope = this;
        return new Scoped(restored);
    }

    /// <summary>
    /// The type parameters the specialized declaration carries: the family's own, with each pack
    /// slot replaced in place by the elements it expands to. In place, because a pack may sit
    /// anywhere in the list and moving it would reorder the arguments every use site writes.
    /// </summary>
    public IReadOnlyList<TypeParameter> Parameters(IReadOnlyList<TypeParameter> declaredParameters)
    {
        var parameters = new List<TypeParameter>();

        foreach(var declared in declaredParameters)
        {
            var index = IndexOf(declared);

            if(index >= 0)
            {
                parameters.AddRange(expansions[index]);
                continue;
            }

            parameters.Add(declared);
        }

        return parameters;
    }

    /// <summary>
    /// The element a pack's <paramref name="index"/> stands for here, or null when the pack is not
    /// one this specialization fixes or the index falls outside it.
    /// </summary>
    public IMistType? Element(TypeParameter pack, int index)
    {
        var expansion = Expansion(pack);
        return index >= 0 && index < expansion.Count ? expansion[index] : null;
    }

    private int IndexOf(TypeParameter pack)
    {
        for(var i = 0; i < Packs.Count; i++)
        {
            if(ReferenceEquals(Packs[i], pack))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class Scoped(Monomorph? restored) : IDisposable
    {
        public void Dispose()
        {
            inScope = restored;
        }
    }
}