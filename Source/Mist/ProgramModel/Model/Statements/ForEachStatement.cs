using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A foreach loop: <c>foreach (T name in collection) { body }</c>.
/// </summary>
internal sealed class ForEachStatement(
    LocalVariable variable,
    Expression collection,
    Block body,
    ImplicitConversionExpression? elementConversion = null) : Statement
{
    public LocalVariable Variable { get; } = variable;

    public Expression Collection { get; } = collection;

    public ImplicitConversionExpression? ElementConversion { get; } = elementConversion;

    public Block Body { get; } = body;

    /// <summary>
    /// The members the loop calls, recorded because the emitted foreach lets Roslyn resolve them
    /// again while a rule judging what the loop touches cannot. All null over an intrinsic sequence
    /// -- an array, a span, a string -- whose iteration the target supplies.
    /// </summary>
    public Method? GetEnumerator { get; init; }

    public Method? MoveNext { get; init; }

    public Property? Current { get; init; }
}