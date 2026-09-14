using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A declaration that spreads a tuple across several locals: <c>var (a, b) = pair;</c> and
/// <c>(int a, string b) = pair;</c>. One kind for both forms, which differ only in where each target's
/// type came from -- inferred from the element or written at the target -- and not in what the statement
/// does.
/// </summary>
internal sealed class TupleDeconstructionStatement(
    IReadOnlyList<LocalVariable> targets,
    Expression initializer,
    IReadOnlyList<ImplicitConversionExpression?>? elementConversions = null) : Statement
{
    /// <summary>
    /// The declared locals in positional order, one per tuple element.
    /// </summary>
    public IReadOnlyList<LocalVariable> Targets { get; } = targets;

    public Expression Initializer { get; } = initializer;

    public IReadOnlyList<ImplicitConversionExpression?> ElementConversions { get; } = elementConversions ?? [.. targets.Select(_ => (ImplicitConversionExpression?)null)];
}