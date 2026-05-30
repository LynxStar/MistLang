using System.Reflection;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Raw metadata for a single C# extension method discovered in a .NET assembly.
/// </summary>
public sealed class AmbientExtensionMethodInfo
{
    /// <summary>The CLR namespace of the containing static class.</summary>
    public required string Namespace { get; init; }

    /// <summary>The full name of the containing static class (for emission).</summary>
    public required string ContainingClassName { get; init; }

    /// <summary>The method name.</summary>
    public required string MethodName { get; init; }

    /// <summary>The CLR Type of the first parameter (receiver).</summary>
    public required Type ReceiverClrType { get; init; }

    /// <summary>The remaining CLR parameters (excluding receiver).</summary>
    public required IReadOnlyList<ParameterInfo> RemainingParameters { get; init; }

    /// <summary>The CLR return type.</summary>
    public required Type ReturnClrType { get; init; }

    /// <summary>Whether the method is a generic method definition.</summary>
    public required bool IsGenericMethod { get; init; }

    /// <summary>Generic type parameters on the method (empty if non-generic).</summary>
    public required IReadOnlyList<Type> MethodTypeParameters { get; init; }

    /// <summary>The MethodInfo for emission metadata.</summary>
    public required MethodInfo MethodInfo { get; init; }
}
