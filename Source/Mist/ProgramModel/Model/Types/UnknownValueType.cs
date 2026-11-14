using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A dependent type argument whose value is not known at compile time, but whose DOMAIN is.
/// The counterpart to <see cref="LiftedType"/>, differing only in whether the value is available:
/// the length of a CLR <c>int[]</c> is one of these over <c>Int32</c>.
/// <para>
/// Distinct from <see cref="UnknownType"/>, the top type that lowers to C# <c>object</c>:
/// <c>unknown</c> alone cannot say what it is unknown OF, so a length read back through it could
/// only be emitted as <c>object</c>.
/// </para>
/// </summary>
internal sealed class UnknownValueType : MistType
{
    /// <remarks>
    /// Named <c>unknown</c> so a constructed type reads the way diagnostics say it. The name is not
    /// the identity -- <see cref="Domain"/> is -- so two domains sharing this name is not a collision.
    /// </remarks>
    public UnknownValueType(IMistType domain) : base("unknown", new Scope(null), declaringType: null)
    {
        Domain = domain;
    }

    public IMistType Domain { get; }
}