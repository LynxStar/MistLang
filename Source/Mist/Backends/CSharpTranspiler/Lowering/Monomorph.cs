using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// One variadic family specialized to one arity: the C# declaration a program that used the family
/// at that width needs, since C# has no pack to declare it with once.
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

    private Monomorph(TypeParameter pack, IReadOnlyList<TypeParameter> expansion)
    {
        Pack = pack;
        Expansion = expansion;
    }

    /// <summary>
    /// The specialization being emitted, if any. A method rather than a property because the field
    /// behind it is thread-static, which an auto-property cannot be.
    /// </summary>
    public static Monomorph? InScope()
    {
        return inScope;
    }

    public TypeParameter Pack { get; }

    /// <summary>
    /// The C# type parameters the pack became, one per element: <c>T1</c> .. <c>TN</c>. Empty for
    /// the arity-zero specialization, which is a real one -- a family used with no arguments needs a
    /// declaration as much as any other.
    /// </summary>
    public IReadOnlyList<TypeParameter> Expansion { get; }

    /// <summary>
    /// The pack a family declares, or null when it declares none and needs no specialization.
    /// </summary>
    public static TypeParameter? PackOf(MistType definition)
    {
        return definition is IHasTypeParameters generic
            ? generic.TypeParametersList.Find(parameter => parameter.PackKind != TypePackKind.None)
            : null;
    }

    /// <summary>
    /// The pack a declaration -- a type or a method -- declares, or null when it declares none.
    /// </summary>
    public static TypeParameter? PackIn(IReadOnlyList<TypeParameter> parameters)
    {
        return parameters.FirstOrDefault(parameter => parameter.PackKind != TypePackKind.None);
    }

    /// <summary>
    /// Whether this specialization is the one a declaration carrying these parameters is being
    /// emitted under. Asked of the PACK rather than of an owner, so a type and a method specialize
    /// through the same mechanism.
    /// </summary>
    public bool Owns(IReadOnlyList<TypeParameter> parameters)
    {
        return parameters.Any(parameter => ReferenceEquals(parameter, Pack));
    }

    public static Monomorph? For(TypeParameter? pack, int arity)
    {
        if(pack is null)
        {
            return null;
        }

        var expansion = new List<TypeParameter>(arity);

        for(var i = 0; i < arity; i++)
        {
            expansion.Add(new TypeParameter($"T{i + 1}", i, new Scope(null), declaringType: null));
        }

        return new Monomorph(pack, expansion);
    }

    public IDisposable Enter()
    {
        var restored = inScope;
        inScope = this;
        return new Scoped(restored);
    }

    /// <summary>
    /// The type parameters the specialized declaration carries: the family's own, with the pack slot
    /// replaced in place by the elements it expands to. In place, because a pack may sit anywhere in
    /// the list and moving it would reorder the arguments every use site writes.
    /// </summary>
    public IReadOnlyList<TypeParameter> Parameters(IReadOnlyList<TypeParameter> declaredParameters)
    {
        var parameters = new List<TypeParameter>();

        foreach(var declared in declaredParameters)
        {
            if(ReferenceEquals(declared, Pack))
            {
                parameters.AddRange(Expansion);
                continue;
            }

            parameters.Add(declared);
        }

        return parameters;
    }

    /// <summary>
    /// The element the pack's <paramref name="index"/> stands for in this specialization, or null
    /// when the index is outside it.
    /// </summary>
    public IMistType? Element(int index)
    {
        return index >= 0 && index < Expansion.Count ? Expansion[index] : null;
    }

    private sealed class Scoped(Monomorph? restored) : IDisposable
    {
        public void Dispose()
        {
            inScope = restored;
        }
    }
}