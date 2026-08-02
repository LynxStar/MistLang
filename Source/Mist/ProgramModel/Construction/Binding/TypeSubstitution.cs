using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A map rather than an ordinal index: each declaration numbers its own type parameters from zero, so
/// a generic method's first shares an ordinal with its enclosing type's first, and an index would
/// answer for the method's parameter out of the receiver's arguments.
/// </summary>
internal sealed class TypeSubstitution
{
    public static readonly TypeSubstitution Empty = new([]);

    private readonly Dictionary<TypeParameter, IMistType> bindings;

    private TypeSubstitution(Dictionary<TypeParameter, IMistType> bindings)
    {
        this.bindings = bindings;
    }

    public static TypeSubstitution ForReceiver(ConstructedType? receiver)
    {
        if(receiver?.Definition is not IHasTypeParameters definition)
        {
            return Empty;
        }

        return ByOrdinal(definition.TypeParametersList, receiver.TypeArguments);
    }

    /// <param name="typeArguments">
    /// A null entry is skipped rather than bound, leaving its parameter for <see cref="Unbound"/>.
    /// </param>
    public static TypeSubstitution ForMethod(Method method, IReadOnlyList<IMistType?> typeArguments)
    {
        return ByOrdinal(method.TypeParametersList, typeArguments);
    }

    /// <summary>
    /// The ordinal indexes the arguments, not the parameter's position in
    /// <paramref name="parameters"/>. They part for a projected extension block, whose parameters keep
    /// the ordinals of the flat CLR list they were partitioned out of.
    /// </summary>
    private static TypeSubstitution ByOrdinal(
        IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType?> arguments)
    {
        var bindings = new Dictionary<TypeParameter, IMistType>();

        foreach(var parameter in parameters)
        {
            if(parameter.Ordinal < arguments.Count && arguments[parameter.Ordinal] is { } argument)
            {
                bindings[parameter] = argument;
            }
        }

        return bindings.Count == 0 ? Empty : new TypeSubstitution(bindings);
    }

    /// <summary>
    /// <see cref="ForReceiver"/> and <see cref="ForMethod"/> bind parameters of different
    /// declarations, so no key is in both maps and the last write below never loses a binding.
    /// </summary>
    public TypeSubstitution Extend(TypeSubstitution other)
    {
        if(other.bindings.Count == 0)
        {
            return this;
        }

        if(bindings.Count == 0)
        {
            return other;
        }

        var combined = new Dictionary<TypeParameter, IMistType>(bindings);

        foreach(var (parameter, argument) in other.bindings)
        {
            combined[parameter] = argument;
        }

        return new TypeSubstitution(combined);
    }

    /// <summary>
    /// An array needs no arm of its own, being a construction of <c>Standard.Array</c>. Nothing but a
    /// construction is descended into, so a parameter reachable solely through a union, a tuple or a
    /// refinement survives untouched and unreported -- a union over a type parameter does resolve, so
    /// that is a reachable gap rather than an unreachable one.
    /// </summary>
    /// <param name="interner">
    /// Null from the emitter's struct flattening, which has none: the interner belongs to construction
    /// and nothing hands it across. Without one the construction built here is equal to the interned
    /// one but not the same object, which the conversion relation does not mind, since it compares
    /// types by qualified name.
    /// </param>
    public IMistType Apply(IMistType type, TypeInterner? interner)
    {
        if(bindings.Count == 0)
        {
            return type;
        }

        if(type is TypeParameter parameter)
        {
            return bindings.TryGetValue(parameter, out var argument) ? argument : type;
        }

        if(type is not ConstructedType constructed)
        {
            return type;
        }

        var substituted = new IMistType[constructed.TypeArguments.Count];
        var changed = false;

        for(var i = 0; i < substituted.Length; i++)
        {
            substituted[i] = Apply(constructed.TypeArguments[i], interner);
            changed |= !ReferenceEquals(substituted[i], constructed.TypeArguments[i]);
        }

        if(!changed)
        {
            return type;
        }

        return interner is null
            ? new ConstructedType(constructed.Definition, substituted)
            : interner.GetOrCreateConstructedType(constructed.Definition, substituted)
            ;
    }

    public IReadOnlyList<TypeParameter> Unbound(IReadOnlyList<TypeParameter> parameters)
    {
        List<TypeParameter>? unbound = null;

        foreach(var parameter in parameters)
        {
            if(!bindings.ContainsKey(parameter))
            {
                unbound ??= [];
                unbound.Add(parameter);
            }
        }

        return unbound ?? [];
    }
}