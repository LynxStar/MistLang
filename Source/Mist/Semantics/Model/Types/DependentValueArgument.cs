using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a concrete dependent value in a constructed type (e.g., the <c>5</c> in <c>FixedBuffer&lt;5&gt;</c>).
/// </summary>
public sealed class DependentValueArgument
{
    /// <summary>The compile-time constant value.</summary>
    public object Value { get; }

    /// <summary>The type of the value (int, bool, char, etc.).</summary>
    public IType ValueType { get; internal set; }

    /// <summary>Display string for diagnostics (e.g., "5", "true", "'x'").</summary>
    public string DisplayValue { get; }

    public DependentValueArgument(object value, IType valueType, string displayValue)
    {
        Value = value;
        ValueType = valueType;
        DisplayValue = displayValue;
    }
}