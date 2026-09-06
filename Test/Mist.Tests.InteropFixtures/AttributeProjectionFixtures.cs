namespace Mist.Tests.InteropFixtures;

public enum ProjectionMarkerLevel
{
    Low,
    High,
}

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public sealed class ProjectionMarkerAttribute : Attribute
{
    public ProjectionMarkerAttribute(string label)
    {
        Label = label;
    }

    public ProjectionMarkerAttribute(
        string label,
        Type targetType,
        ProjectionMarkerLevel level,
        int[] values)
    {
        Label = label;
        TargetType = targetType;
        Level = level;
        Values = values;
    }

    public string Label { get; }

    public Type? TargetType { get; }

    public ProjectionMarkerLevel Level { get; }

    public int[] Values { get; } = [];

    public int Order { get; set; }

    public string Category = "";
}

[ProjectionMarker(
    "type",
    typeof(string),
    ProjectionMarkerLevel.High,
    new[] { 2, 4 },
    Order = 1,
    Category = "root")]
public class AttributeProjectionTarget
{
    [ProjectionMarker("field", Order = 2)]
    public int Value;

    [ProjectionMarker("property", Order = 3)]
    public int Number { get; set; }

    [ProjectionMarker("constructor", Order = 4)]
    public AttributeProjectionTarget(
        [ProjectionMarker("constructor-parameter", Order = 5)] int value)
    {
        Value = value;
        Number = value;
    }

    [ProjectionMarker("method", Order = 6)]
    public int Transform(
        [ProjectionMarker("method-parameter", Order = 7)] int value)
    {
        return value * 2;
    }

    [ProjectionMarker("indexer", Order = 8)]
    public int this[[ProjectionMarker("indexer-parameter", Order = 9)] int index]
    {
        get => Number + index; set => Number = value - index;
    }

    [ProjectionMarker("operator", Order = 10)]
    public static AttributeProjectionTarget operator +(
        [ProjectionMarker("operator-left-parameter", Order = 11)] AttributeProjectionTarget left,
        [ProjectionMarker("operator-right-parameter", Order = 12)] AttributeProjectionTarget right)
    {
        return new AttributeProjectionTarget(left.Value + right.Value);
    }

    [ProjectionMarker("cast", Order = 13)]
    public static implicit operator int(
        [ProjectionMarker("cast-parameter", Order = 14)] AttributeProjectionTarget value)
    {
        return value.Value;
    }

    public int ReadIn(in int value)
    {
        return value;
    }

    public void WriteOut(out int value)
    {
        value = Number;
    }

    public void Mutate(ref int value)
    {
        value = Number;
    }

    /// <summary>
    /// A mutable <c>ref</c> carrying the interop <c>[In]</c> annotation. Its metadata In flag is set
    /// exactly as a real <c>in</c> parameter's is, and it still requires <c>ref</c> at a call site --
    /// which is why the flag alone cannot be read as readonly-ness. COM declares 43 of these in the
    /// WindowsDesktop reference pack.
    /// </summary>
    public void MutateMarkedIn([System.Runtime.InteropServices.In] ref int value)
    {
        value = Number;
    }

    /// <summary>
    /// Both interop flags on one mutable <c>ref</c>. Sets the Out flag as well, so an out-test that
    /// does not exclude In would take it for an <c>out</c> parameter and emit the wrong keyword.
    /// </summary>
    public void MutateMarkedInOut(
        [System.Runtime.InteropServices.In][System.Runtime.InteropServices.Out] ref int value)
    {
        value = Number;
    }

    public int ReadRefReadonly(ref readonly int value)
    {
        return value;
    }

    public int CountParams(params string[] values)
    {
        return values.Length;
    }
}

public readonly struct ReadonlyAttributeProjectionTarget
{
    public ReadonlyAttributeProjectionTarget(int value)
    {
        Value = value;
    }

    public int Value { get; }
}