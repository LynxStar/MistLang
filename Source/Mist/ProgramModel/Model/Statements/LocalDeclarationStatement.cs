using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Statements;

internal sealed class LocalDeclarationStatement(LocalVariable local, Expression? initializer) : Statement
{
    public LocalVariable Local { get; } = local;

    public Expression? Initializer { get; } = initializer;

    public IReadOnlyList<SlotCapture> Captures { get; init; } = [];
}