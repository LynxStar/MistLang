using System.Reflection;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// ProgramModel-level descriptor for a CLR extension method discovered in a loaded .NET assembly.
/// Contains raw CLR metadata sufficient for the ambient type provider to project extension methods
/// into the ProgramModel without depending on backend or semantic-model types.
/// </summary>
/// <param name="MethodName">The name of the extension method.</param>
/// <param name="ReceiverClrType">The CLR type of the first parameter (the receiver being extended).</param>
/// <param name="ContainingClassName">The full name of the static class that declares this extension method.</param>
/// <param name="RemainingParameters">The CLR parameters after the receiver (excluding the first parameter).</param>
/// <param name="ReturnClrType">The CLR return type of the extension method.</param>
/// <param name="IsGenericMethodDefinition">Whether the method is an open generic method definition.</param>
/// <param name="GenericTypeParameters">The generic type parameters declared on the method (empty if non-generic).</param>
public record AmbientExtensionMethodInfo(
    string MethodName,
    Type ReceiverClrType,
    string ContainingClassName,
    IReadOnlyList<ParameterInfo> RemainingParameters,
    Type ReturnClrType,
    bool IsGenericMethodDefinition,
    IReadOnlyList<Type> GenericTypeParameters);