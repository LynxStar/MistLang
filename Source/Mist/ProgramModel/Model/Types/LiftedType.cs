namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A value that has been lifted from the runtime universe into the type universe
/// so it can participate as a type argument. Represents compile-time constants
/// (integer, boolean, char literals) used in dependent type argument positions
/// (e.g., the <c>5</c> in <c>Buffer&lt;5&gt;</c>).
/// <para>
/// Interned by value: there is exactly one <see cref="LiftedType"/> per unique
/// compile-time constant across the entire program. Multiple constructed types
/// using the same literal share the same instance.
/// </para>
/// </summary>
internal sealed class LiftedType : MistType
{
    public LiftedType(object value, string displayValue)
        : base(displayValue, "", new Scope(null), declaringType: null)
    {
        Value = value;
        DisplayValue = displayValue;
    }

    /// <summary>
    /// The compile-time constant value (boxed long, bool, or char).
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// The display representation of the value for diagnostics and name building
    /// (e.g., "5", "-1", "true", "'x'", "0xFF").
    /// </summary>
    public string DisplayValue { get; }
}