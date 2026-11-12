namespace Mist.Driver.Contracts;

/// <summary>What a <c>[references]</c> entry names; <see cref="ReferenceClassifier"/> decides it.</summary>
public enum ReferenceKind
{
    MistProject,
    CSharpProject,
    Assembly
}