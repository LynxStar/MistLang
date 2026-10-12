namespace Mist.Tests.InteropFixtures;

public sealed class SerializerOptions
{
    public bool Indented { get; set; }
}

// The callable shapes JsonSerializer.Serialize and Path.Combine declare: an optional trailing parameter
// beside an overload that requires one, and a params array beside a params span.
public static class CallableShapes
{
    public static string Serialize<TValue>(TValue value, SerializerOptions? options = null)
    {
        return options is { Indented: true } ? $"  {value}" : $"{value}";
    }

    public static string Serialize<TValue>(TValue value, SerializerOptions options, string label)
    {
        return $"{label}: {Serialize(value, options)}";
    }

    public static string Combine(string first, string second)
    {
        return first + "/" + second;
    }

    public static string Combine(string first, string second, string third)
    {
        return Combine(Combine(first, second), third);
    }

    public static string Combine(string first, string second, string third, string fourth)
    {
        return Combine(Combine(first, second, third), fourth);
    }

    public static string Combine(params string[] parts)
    {
        return string.Join("/", parts);
    }

    public static string Combine(params ReadOnlySpan<string> parts)
    {
        return string.Join("/", parts.ToArray());
    }
}