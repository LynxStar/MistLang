namespace Mist.ProgramAnalysis;

[Flags]
internal enum AccessDirection
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}