using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A resolved retroactive conformance declaration.
/// </summary>
internal sealed class ConformanceDeclaration(
    IMistType conformingType,
    IMistType abstractType,
    string namespaceName,
    IReadOnlyList<MemberMapping> mappings) : IConformanceDeclaration
{
    public IMistType ConformingType { get; } = conformingType;

    public IMistType AbstractType { get; } = abstractType;

    public string NamespaceName { get; } = namespaceName;

    public IReadOnlyList<MemberMapping> Mappings { get; } = mappings;

    IReadOnlyList<IMemberMapping> IConformanceDeclaration.Mappings => Mappings;
}

/// <summary>
/// A single resolved member mapping within a conformance declaration.
/// </summary>
internal sealed class MemberMapping(string sourceName, string targetName) : IMemberMapping
{
    public string SourceName { get; } = sourceName;

    public string TargetName { get; } = targetName;
}