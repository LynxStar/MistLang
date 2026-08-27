using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An <c>as</c> conversion, as <c>animal as Dog</c>. Its result type is the CLASSIFICATION'S, which is
/// where Mist parts company with C#: a conversion that cannot fail answers the bare target, and only a
/// runtime-checked one answers an optional, where C# always answers a nullable. So both types are
/// carried -- <see cref="TargetType"/> spells the test, <see cref="Type"/> spells what the test produces.
/// <para>
/// <see cref="Kind"/> and <see cref="Constituent"/> are resolved here rather than left to emission, for
/// the reason <see cref="Patterns.TypeTestPattern.Constituent"/> gives: the lookup that classifies the
/// conversion is the same one that decides whether there is any C# to write for it.
/// </para>
/// <para>
/// The value-versus-reference split that picks between C#'s two runtime-checked spellings is
/// deliberately NOT modelled. It is a fact about the emitted language rather than about the conversion,
/// so <see cref="AsConversionKind.Narrowing"/> covers both and the backend chooses.
/// </para>
/// </summary>
internal sealed class AsExpression(
    SyntaxNode node,
    Expression operand,
    IMistType targetType,
    AsConversionKind kind,
    IMistType? constituent,
    IMistType resultType) : Expression(node)
{
    public Expression Operand { get; } = operand;

    public IMistType TargetType { get; } = targetType;

    public AsConversionKind Kind { get; } = kind;

    public IMistType? Constituent { get; } = constituent;

    public override IMistType Type { get; } = resultType;
}

internal enum AsConversionKind
{
    Identity,
    Widening,
    Narrowing,
    UnionExtraction,
}