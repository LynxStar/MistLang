using System.Globalization;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A value that has been lifted from the runtime universe into the type universe
/// so it can participate as a type argument. Represents compile-time constants
/// (integer, boolean, char literals) used in dependent type argument positions
/// (e.g., the <c>5</c> in <c>Buffer&lt;5&gt;</c>).
/// <para>
/// Identity is the pair (<see cref="Type"/>, <see cref="Value"/>). The Mist type is
/// what separates <c>255</c> as an <c>int</c> from <c>255</c> as a <c>byte</c>; the
/// value is compared numerically, so <c>0xFF</c> and <c>255</c> lift to the same type.
/// </para>
/// </summary>
internal sealed class LiftedType : MistType
{
    public LiftedType(IMistType type, object value)
        : base(value.ToString(), "", new Scope(null), declaringType: null)
    {
        Type = type;
        Value = value;
    }

    /// <summary>
    /// The Mist type of the lifted value (e.g. <c>Standard.Int32</c> for <c>5</c>).
    /// </summary>
    public IMistType Type { get; }

    /// <summary>
    /// The compile-time constant value. Integral values are normalized to
    /// <see cref="long"/>; <see cref="Type"/> carries which Mist type they belong to.
    /// </summary>
    public object Value { get; }

}