using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An explicit cast written in source, as <c>(Kelvin)celsius</c>. Which conversion it performs is
/// Mist's decision, so <see cref="Operator"/> is resolved at binding by
/// <see cref="Construction.Binding.ConversionRelation.FindOperator"/> rather than left to the emitted
/// C#, whose null and non-transitivity rules that method documents.
/// <para>
/// No emitter reads it. C# refuses to call a conversion operator by name (CS0571), leaving a cast as
/// the only spelling and Roslyn to resolve it again, so the two answers agree by coincidence. The
/// field is what a second backend, or a rule refusing an illegal cast, would read.
/// </para>
/// </summary>
internal sealed class CastExpression(
    SyntaxNode node,
    Expression operand,
    IMistType targetType,
    CastOperator? conversionOperator) : Expression(node)
{
    public Expression Operand { get; } = operand;

    public CastOperator? Operator { get; } = conversionOperator;

    public override IMistType Type { get; } = targetType;
}