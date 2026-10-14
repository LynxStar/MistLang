namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// How a site reaches a member's storage. A method call is a <see cref="Read"/>: it has one classification,
/// its body's, and the two directions are what a property or an indexer separate into.
/// </summary>
[Flags]
internal enum StorageAccess
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}