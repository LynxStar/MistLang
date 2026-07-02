using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// A delegate / function type (e.g., (int, int) -> string).
/// </summary>
internal sealed class DelegateTypeReference(
    IReadOnlyList<ITypeReference> parameterTypes,
    ITypeReference returnType) : IDelegateTypeReference
{
    public IReadOnlyList<ITypeReference> ParameterTypes { get; } = parameterTypes;

    public ITypeReference ReturnType { get; } = returnType;

    public string DisplayName { get; } =
        $"({string.Join(", ", parameterTypes.Select(p => p.DisplayName))}) -> {returnType.DisplayName}";
}