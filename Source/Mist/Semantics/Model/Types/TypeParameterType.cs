using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents an unsubstituted type parameter (e.g., <c>T</c> in <c>class Box&lt;T&gt;</c>).
/// Type parameters are treated as reference types by default (Kind = TypeKind.Class).
/// </summary>
public sealed class TypeParameterType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Class;
    public string Name { get; }

    /// <summary>
    /// Position in the type parameter list (0-based).
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// The constraints declared on this type parameter.
    /// Null if no constraints are declared (unconstrained).
    /// </summary>
    public TypeParameterConstraints? Constraints { get; internal set; }

    /// <summary>
    /// True when this type parameter is a variadic pack (declared <c>params TArgs</c>
    /// or <c>Type[]:TArgs</c> / <c>Type[N]:TArgs</c>). A pack stands for an ordered,
    /// comptime-known list of type arguments; its value domain is the metatype
    /// array <c>Type[]</c>. Everything else about it (name, ordinal, constraints) is
    /// an ordinary type parameter, which is what lets a pack reuse the generics
    /// machinery.
    /// </summary>
    public bool IsPack { get; init; }

    /// <summary>
    /// True when this pack is the single bracket-free (unbracketed) pack in its list —
    /// declared with <c>params</c>, or a lone <c>Type[]:</c> pack promoted by inverse
    /// sugar. The unbracketed pack absorbs the contiguous middle arguments at the use
    /// site; any other pack in the list must be supplied bracketed. Only meaningful
    /// when <see cref="IsPack"/> is true.
    /// </summary>
    public bool IsUnbracketedPack { get; init; }

    /// <summary>
    /// For a fixed-size pack (<c>Type[N]:TArgs</c>), the exact arity <c>N</c>. Null for
    /// an unknown-length pack (<c>Type[]:TArgs</c> / <c>params TArgs</c>). Only
    /// meaningful when <see cref="IsPack"/> is true.
    /// </summary>
    public int? PackFixedArity { get; init; }

    public IType? ClrInboundVoid { get; init; }

    public TypeParameterType(string name, int ordinal)
    {
        Name = name;
        DisplayName = name;
        Ordinal = ordinal;
    }

    public TypeParameterType AsClrInbound(IType voidType)
    {
        return new TypeParameterType(Name, Ordinal)
        {
            Constraints = Constraints,
            IsPack = IsPack,
            IsUnbracketedPack = IsUnbracketedPack,
            PackFixedArity = PackFixedArity,
            ClrInboundVoid = voidType,
        };
    }

    /// <summary>
    /// Closes this parameter over a substituted type, adding <c>| void</c> when the CLR could have
    /// handed back a null there. Whether it could is settled by <see cref="ClrInboundVoid"/>, which
    /// only TypeProjector's inbound-result path sets -- so "came from the CLR" holds by construction
    /// rather than by a discriminator that can be laundered, and a Mist-declared generic can never
    /// be wrapped however its argument substitutes.
    /// <para>
    /// BOTH EARLY EXITS GUARD A PROPERTY THAT IS INVISIBLE AT THE CALL SITE. ErrorType has to leave
    /// before <see cref="IType.Kind"/> is read at all, because reading it throws. A still-open
    /// parameter has to leave because the Kind this very class reports is Class -- see the type
    /// summary -- so the reference-type test would say yes to something not yet substituted.
    /// </para>
    /// </summary>
    public IType Close(IType substituted)
    {
        if(ClrInboundVoid is null || substituted is ErrorType or TypeParameterType)
        {
            return substituted;
        }

        if(substituted.Kind is not (TypeKind.Class or TypeKind.Abstract))
        {
            return substituted;
        }

        return new UnionType(new List<IType> { substituted, ClrInboundVoid })
        {
            NullableProjection = new NullableProjectionInfo(NullableOrigin.ReferenceType, substituted),
        };
    }
}