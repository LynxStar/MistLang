namespace Mist.Tests.InteropFixtures;

/// <summary>
/// One enum per legal CLR base, each declaring that base's two extremes, plus the sparse, aliased
/// and flag shapes a projection could get wrong by counting. Used by <c>ClrEnumConstantProjectionTests</c>.
/// </summary>
public enum SByteBounds : sbyte
{
    Min = sbyte.MinValue,
    Max = sbyte.MaxValue,
}

public enum ByteBounds : byte
{
    Min = byte.MinValue,
    Max = byte.MaxValue,
}

public enum Int16Bounds : short
{
    Min = short.MinValue,
    Max = short.MaxValue,
}

public enum UInt16Bounds : ushort
{
    Min = ushort.MinValue,
    Max = ushort.MaxValue,
}

public enum Int32Bounds
{
    Min = int.MinValue,
    Max = int.MaxValue,
}

public enum UInt32Bounds : uint
{
    Min = uint.MinValue,
    Max = uint.MaxValue,
}

public enum Int64Bounds : long
{
    Min = long.MinValue,
    Max = long.MaxValue,
}

public enum UInt64Bounds : ulong
{
    Min = ulong.MinValue,
    Max = ulong.MaxValue,
}

public enum SparseCodes
{
    Negative = -3,
    Counted,
    Zero = 0,
    Far = 1000,
    AliasOfFar = Far,
}

[Flags]
public enum WideFlags : ulong
{
    None = 0,
    Low = 1,
    Middle = 1UL << 32,
    High = 1UL << 63,
    LowAndHigh = Low | High,
}