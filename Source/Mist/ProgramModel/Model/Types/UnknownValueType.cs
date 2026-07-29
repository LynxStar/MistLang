using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A dependent type argument whose value is not known at compile time, but whose DOMAIN is.
/// The counterpart to <see cref="LiftedType"/>: both occupy a dependent parameter's slot and
/// both know the domain, and they differ only in whether the value is available. An array
/// typed <c>int[]</c> from CLR interop is <c>Array&lt;int, unknown-of-int&gt;</c>, where a
/// literal <c>[1, 2, 3]</c> is <c>Array&lt;int, 3&gt;</c>.
/// <para>
/// Distinct from <see cref="UnknownType"/>, which is the user-facing top type that every
/// non-void value is assignable to and which lowers to C# <c>object</c>. Conflating the two
/// loses the only information an emitter has to work with: <c>unknown</c> alone cannot say
/// what it is unknown OF, so a length read back through it can only be emitted as
/// <c>object</c>. Keeping them separate is what lets the domain be recovered.
/// </para>
/// <para>
/// The domain is supplied by the construction site rather than read off the type parameter
/// being filled, because <see cref="TypeParameter.Domain"/> is not populated yet. When it is,
/// this should carry the parameter and read the domain through it, which also generalizes it
/// past the one construction site that exists today.
/// </para>
/// </summary>
internal sealed class UnknownValueType : MistType
{
    /// <remarks>
    /// Named <c>unknown</c> so a constructed type reads as <c>Array&lt;string, unknown&gt;</c>,
    /// which is what it means to a reader and what diagnostics already say. The name is not the
    /// identity -- <see cref="Domain"/> is -- so two domains sharing this name is not a collision.
    /// </remarks>
    public UnknownValueType(IMistType domain)
        : base("unknown", new Scope(null), declaringType: null)
    {
        Domain = domain;
    }

    /// <summary>
    /// The type whose values could fill this slot, had one been known. For an array length this
    /// is <c>Standard.Int32</c>.
    /// </summary>
    public IMistType Domain { get; }
}