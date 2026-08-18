using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A single member (variant) of an enum type.
/// </summary>
internal sealed class EnumMember(
    string name, bool isDefault, Expression? value, EnumMemberNode? declarationNode = null) : IEnumMember
{
    /// <summary>The variant name.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// Where the variant was written, so a diagnostic about ONE variant lands on that variant. The
    /// enclosing enum's node is the fallback for members with no syntax behind them.
    /// </summary>
    internal EnumMemberNode? DeclarationNode { get; } = declarationNode;

    /// <summary>Whether this variant is marked as the enum's default.</summary>
    public bool IsDefault { get; } = isDefault;

    /// <summary>
    /// What this variant's underlying value IS. Phase 2 parks a written initializer unbound and
    /// Phase 3 replaces it, as a field initializer is; a variant that writes none has one
    /// SYNTHESIZED there from the inference rule for the base. So every variant carries an
    /// expression by the time anything reads this, and no consumer has to re-derive an omitted value
    /// or reach back to the syntax for a written one.
    /// </summary>
    public Expression? Value { get; internal set; } = value;
}