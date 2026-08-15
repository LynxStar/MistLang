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

    /// <summary>
    /// Where the with block was written, held for the backend refusal in
    /// <see cref="Common.Diagnostics.BackendDiagnostics.ForeignConformance"/>. That refusal is
    /// reported against a conforming type this backend never emits, so it cannot reach a position in
    /// Mist source any other way. Mirrors how <see cref="Construction.Resolution.ConstraintClosure"/>
    /// carries a node for the sibling refusal.
    /// </summary>
    internal SyntaxNode DeclarationNode { get; } = declarationNode;

    IReadOnlyList<IMemberMapping> IConformanceDeclaration.Mappings => Mappings;
}

/// <summary>
/// A slot of the abstract type paired with the member of the conforming type that answers it.
/// </summary>
internal abstract class MemberMapping(string sourceName, string targetName, IMistType declarer) : IMemberMapping
{
    public string SourceName { get; } = sourceName;

    public string TargetName { get; } = targetName;

    /// <summary>
    /// The abstract that DECLARES the slot, which is not always the one the with block names -- a
    /// block may map onto a slot the named abstract inherits. Recorded because an explicit interface
    /// implementation in C# has to name the declaring interface, and naming the inheriting one is
    /// rejected outright rather than resolved through it.
    /// </summary>
    public IMistType Declarer { get; } = declarer;
}

/// <summary>
/// A method slot answered by a method.
/// </summary>
internal sealed class MethodMapping(Method source, Method target, IMistType declarer)
    : MemberMapping(source.Name, target.Name, declarer)
{
    public Method Source { get; } = source;

    public Method Target { get; } = target;
}

/// <summary>
/// A property slot answered by a property.
/// </summary>
internal sealed class PropertyMapping(Property source, Property target, IMistType declarer)
    : MemberMapping(source.Name, target.Name, declarer)
{
    public Property Source { get; } = source;

    public Property Target { get; } = target;
}