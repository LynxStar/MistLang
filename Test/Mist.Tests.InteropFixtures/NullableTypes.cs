namespace Mist.Tests.InteropFixtures;

public class HasNullableMembers
{
    public int? GetNullableInt(bool returnNull)
    {
        return returnNull ? null : 42;
    }

    public string? GetNullableString(bool returnNull)
    {
        return returnNull ? null : "hello";
    }

    public int DoubleOrDefault(int? value)
    {
        return value.HasValue ? value.Value * 2 : 0;
    }

    public string ValueOrFallback(string? value, string fallback)
    {
        return value ?? fallback;
    }

    /// <summary>Control: not projected as T|void.</summary>
    public int GetPlainInt()
    {
        return 42;
    }

    /// <summary>Control: not projected as T|void.</summary>
    public SimpleStruct GetPlainStruct()
    {
        return new(1, 2);
    }
}

public interface IHasNullableSlots
{
    string Describe(string? format, SimpleClass? context);
}