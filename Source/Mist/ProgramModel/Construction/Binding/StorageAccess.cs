namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A method call is a <see cref="Read"/>: it has one classification, its body's. Only a property or an
/// indexer separates into the two directions.
/// </summary>
[Flags]
internal enum StorageAccess
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}