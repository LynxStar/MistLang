using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A retroactive <c>with Foo : IBar { ... }</c>, active only where its declaring namespace is imported.
/// </summary>
public interface IConformanceDeclaration
{
    IMistType ConformingType { get; }

    IMistType AbstractType { get; }

    string NamespaceName { get; }

    IReadOnlyList<IMemberMapping> Mappings { get; }
}

public interface IMemberMapping
{
    /// <summary>The conforming type's member.</summary>
    string SourceName { get; }

    /// <summary>The abstract's slot it satisfies.</summary>
    string TargetName { get; }
}