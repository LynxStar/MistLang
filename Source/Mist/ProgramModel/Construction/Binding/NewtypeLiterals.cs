using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

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
        if(value is ArrayLiteralExpression { ElementType: { } element } array && ConversionRelation.CloneSourceOf(element) is not null)
        {
            return new ArrayLiteralExpression(
                array.Node, element, [.. array.Elements.Select(item => Admit(item, element, interner)!)], array.Type)
            { IsTargetTyped = array.IsTargetTyped };
        }

        if(value is null
            || target is null
            || ConversionRelation.CloneSourceOf(target) is not { } source
            || !IsLiteral(value))
        {
            return value;
        }

        var entering = ConversionRelation.CloneSourceOf(source) is null ? value : Admit(value, source, interner)!;

        if(!Fits(entering, source, interner)
            || ConversionRelation.DeclaredOn(target).FirstOrDefault(function => function.Operator is { CopiesClone: true, IsImplicit: false }) is not { } conversion)
        {
            return value;
        }

        return new CastExpression(value.Node, entering, target, [conversion], target);
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