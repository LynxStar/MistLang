namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Storage evidence is independent of lookup visibility. An incomplete layout retains its known
/// fields but also requires an opaque remainder; a null field identity cannot be assigned by lookup.
/// </summary>
public sealed record StorageLayout(bool IsComplete, IReadOnlyList<StorageField> Fields, IMistType? BaseType = null);

public sealed record StorageField(string Name, IMistType Type, IField? Field = null);