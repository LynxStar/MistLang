using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A literal written directly into a slot whose newtype is in sight enters it as though cast (newtypes spec
/// Requirement 9): the binder calls this only at the slots Requirement 9.2 names, with the value bound there,
/// so a literal nested inside that value -- an operand, a branch -- and a call argument, whose parameter type
/// sits in another declaration, never reach it. The conversion is recorded as the cast it stands for, which
/// every later stage already judges and lowers.
/// </summary>
internal static class NewtypeLiterals
{
    public static Expression? Admit(Expression? value, IMistType? target, TypeInterner interner)
    {
        if(value is ArrayLiteralExpression { ElementType: ConcreteType { CloneSource: not null } element } array)
        {
            return new ArrayLiteralExpression(
                array.Node, element, [.. array.Elements.Select(item => Admit(item, element, interner)!)], array.Type);
        }

        if(value is null
            || target is not ConcreteType { CloneSourceType: { } source } clone
            || !IsLiteral(value)
            || !Fits(value, source, interner)
            || clone.CastOperatorsList.Find(castOperator => castOperator is { CopiesClone: true, IsImplicit: false }) is not { } conversion)
        {
            return value;
        }

        return new CastExpression(value.Node, value, clone, [ConversionFunction.Unsubstituted(conversion)], clone);
    }

    private static bool IsLiteral(Expression value)
    {
        return value is LiteralExpression { Value: not null }
            or UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression { Value: not (null or string or bool or char) } };
    }

    private static bool Fits(Expression value, IMistType source, TypeInterner interner)
    {
        return ConversionClassifier.Classify(value.Type, source, interner) is ConversionKind.Identity or ConversionKind.Implicit
            || ConstantConversion.Fits(value, source);
    }
}