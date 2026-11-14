using System.Globalization;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A compile-time constant (integer, boolean or char literal) lifted into the type universe to serve
/// as a dependent type argument. Identity is the pair (<see cref="Type"/>, <see cref="Value"/>): the
/// type separates <c>255</c> as an <c>int</c> from <c>255</c> as a <c>byte</c>, and the value is
/// compared numerically, so <c>0xFF</c> and <c>255</c> lift to the same type.
/// </summary>
internal sealed class LiftedType : MistType
{
    public LiftedType(IMistType type, object value)
        : base(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", new Scope(null), declaringType: null)
    {
        Type = type;
        Value = value;
    }

    public IMistType Type { get; }

    /// <summary>
    /// Integral values are normalized to <see cref="long"/>, or to <see cref="ulong"/> above
    /// <see cref="long.MaxValue"/>, so every integral value has exactly one box.
    /// </summary>
    public object Value { get; }
}