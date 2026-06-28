namespace Audit.Models;

public sealed record TypeInfo(
    string Name,
    string Kind,
    bool IsPureData,
    int MethodCount,
    int ExternalDependents);