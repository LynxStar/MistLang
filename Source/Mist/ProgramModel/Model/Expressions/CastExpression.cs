using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
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
/// <para>
/// A cast is sugar for calling a conversion function, so its type is that function's return type
/// rather than the type written. The two part only for an enum reached from a value that is not
/// already one: the enum's lookup function takes its underlying type and answers the optional enum,
/// since no variant need hold the value (enums spec, Requirement 15). <see cref="LookupParameterType"/>
/// is that function's parameter, which the operand is matched against as an argument is.
/// </para>
/// </summary>
internal sealed class CastExpression(
    SyntaxNode node,
    Expression operand,
    IMistType targetType,
    CastOperator? conversionOperator,
    IMistType resultType) : Expression(node)
{
    public Expression Operand { get; } = operand;

    public IMistType TargetType { get; } = targetType;

    public CastOperator? Operator { get; } = conversionOperator;

    public override IMistType Type { get; } = resultType;

    public IMistType? LookupParameterType => TargetType is EnumType { UnderlyingType: { } underlying } && !ReferenceEquals(Type, TargetType) ? underlying : null;
}