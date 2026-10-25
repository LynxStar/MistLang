namespace Mist.Common.Diagnostics;

public sealed record RegistryEntry(Type DeclaringType, string FieldName, MistDiagnostic Descriptor)
{
    public string Field => $"{DeclaringType.Name}.{FieldName}";
}