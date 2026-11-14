using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Members;

internal sealed class EnumMember(
    string name, bool isDefault, Expression? value, EnumMemberNode? declarationNode = null) : IEnumMember
{
    public string Name { get; } = name;

    /// <summary>
    /// Where the variant was written, so a diagnostic about ONE variant lands on that variant. The
    /// enclosing enum's node is the fallback for members with no syntax behind them.
    /// </summary>
    internal EnumMemberNode? DeclarationNode { get; } = declarationNode;

    public bool IsDefault { get; } = isDefault;

    /// <summary>
    /// Phase 2 parks a written initializer unbound and Phase 3 replaces it, as a field initializer is;
    /// a variant that writes none has one SYNTHESIZED there from the inference rule for the base. So
    /// every variant carries an expression by the time anything reads this.
    /// </summary>
    public Expression? Value { get; internal set; } = value;
}