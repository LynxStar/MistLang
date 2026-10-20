namespace Mist.Tests.InteropFixtures;

/// <summary>
/// One literal field per primitive kind a CLR constant can have, negative values among them, beside the
/// static readonly, null and enum-typed fields that carry no projected value. Used by
/// <c>ClrFieldConstantProjectionTests</c>.
/// </summary>
public sealed class FieldConstants
{
    public const sbyte SByteValue = sbyte.MinValue;
    public const byte ByteValue = byte.MaxValue;
    public const short Int16Value = -300;
    public const ushort UInt16Value = ushort.MaxValue;
    public const int Int32Value = -42;
    public const uint UInt32Value = uint.MaxValue;
    public const long Int64Value = long.MinValue;
    public const ulong UInt64Value = ulong.MaxValue;
    public const char CharValue = 'M';
    public const bool BoolValue = true;
    public const float SingleValue = -1.5f;
    public const double DoubleValue = 2.25;
    public const string StringValue = "mist";
    public const string? NullString = null;
    public const SparseCodes EnumValue = SparseCodes.Far;
    public static readonly long ReadonlyValue = 7;
    public static long MutableValue = 7;
}