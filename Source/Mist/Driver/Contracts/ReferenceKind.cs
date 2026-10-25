namespace Mist.Driver.Contracts;

/// <summary>What a <c>[references]</c> entry names, decided from its path or from a directory's own files.</summary>
public enum ReferenceKind
{
    MistProject,
    CSharpProject,
    Assembly
}