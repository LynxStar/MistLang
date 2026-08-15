using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A resolved retroactive conformance declaration.
/// </summary>
internal sealed class ConformanceDeclaration(
    IMistType conformingType,
    IMistType abstractType,
    string namespaceName,
    IReadOnlyList<MemberMapping> mappings,
    SyntaxNode declarationNode) : IConformanceDeclaration
{
    public IMistType ConformingType { get; } = conformingType;

    public IMistType AbstractType { get; } = abstractType;

    public string NamespaceName { get; } = namespaceName;

    public IReadOnlyList<MemberMapping> Mappings { get; } = mappings;

    internal SyntaxNode DeclarationNode { get; } = declarationNode;

    IReadOnlyList<IMemberMapping> IConformanceDeclaration.Mappings => Mappings;
}

internal abstract class MemberMapping(string sourceName, string targetName, IMistType declarer) : IMemberMapping
{
    public string SourceName { get; } = sourceName;

    public string TargetName { get; } = targetName;

    /// <summary>
    /// Not always the abstract the with block names, since a slot can be inherited: C# rejects an
    /// explicit implementation that names an inheriting interface.
    /// </summary>
    public IMistType Declarer { get; } = declarer;
}

internal sealed class MethodMapping(Method source, Method target, IMistType declarer)
    : MemberMapping(source.Name, target.Name, declarer)
{
    public Method Source { get; } = source;

    public Method Target { get; } = target;
}

internal sealed class PropertyMapping(Property source, Property target, IMistType declarer)
    : MemberMapping(source.Name, target.Name, declarer)
{
    public Property Source { get; } = source;

    public Property Target { get; } = target;
}