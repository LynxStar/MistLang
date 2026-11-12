using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// One kind for both <c>var (a, b) = pair;</c> and <c>(int a, string b) = pair;</c>, which differ only in
/// where each target's type came from, not in what the statement does.
/// </summary>
internal sealed class TupleDeconstructionStatement(
    IReadOnlyList<LocalVariable> targets,
    Expression initializer,
    IReadOnlyList<ImplicitConversionExpression?>? elementConversions = null) : Statement
{
    public IReadOnlyList<LocalVariable> Targets { get; } = targets;

    public Expression Initializer { get; } = initializer;

    public IReadOnlyList<ImplicitConversionExpression?> ElementConversions { get; } = elementConversions ?? [.. targets.Select(_ => (ImplicitConversionExpression?)null)];
}