using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Which type a binary operator produces over two operand types, or null where no operator admits them.
/// One home for the binder, which types the expression by it, and the operator rule, which reports by
/// it, so the two cannot disagree about what the language allows. Numeric operands promote as C#
/// promotes them; a string on either side of <c>+</c> concatenates; a declared operator on either
/// operand's type answers for itself, projected CLR operators included; bool takes the logical pair
/// and every equality; a string, a char, a variant of one enum, an anonymous shape, and an intrinsic
/// or projected type against itself take equality, the last because its equality is the CLR's to
/// define and Roslyn's to judge, where a Mist class has none unless it declares one. An indeterminate
/// operand -- a type parameter, an error, a skipped projection -- admits anything, since nothing can
/// be said of it here. Unknown admits NOTHING, which is the unknown-type spec's rule, and a union
/// admits nothing either, its constituents being what an operator would have to agree on.
/// </summary>
internal static class OperatorAdmissibility
{
    public static IMistType? ResultType(
        BinaryOperatorKind op, IMistType left, IMistType right, Func<WellKnownType, IMistType> wellKnown)
    {
        if(IsIndeterminate(left) || IsIndeterminate(right))
        {
            return left;
        }

        if(left is UnknownType || right is UnknownType || left is UnionType || right is UnionType)
        {
            return null;
        }

        if(Declared(op, left, right) is { } declared)
        {
            return declared.ReturnType;
        }

        var leftBase = Underlying(left);
        var rightBase = Underlying(right);

        return op switch
        {
            BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.LogicalOr
                => Is(leftBase, WellKnownTypes.Boolean) && Is(rightBase, WellKnownTypes.Boolean)
                    ? wellKnown(WellKnownTypes.Boolean)
                    : null,
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual
                => Promoted(leftBase, rightBase) is not null
                    || (Is(leftBase, WellKnownTypes.Boolean) && Is(rightBase, WellKnownTypes.Boolean))
                    || (Is(leftBase, WellKnownTypes.String) && Is(rightBase, WellKnownTypes.String))
                    || SameEnum(left, right)
                    || SameForeignOrAnonymous(left, right)
                    ? wellKnown(WellKnownTypes.Boolean)
                    : null,
            BinaryOperatorKind.LessThan or BinaryOperatorKind.GreaterThan
                or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThanOrEqual
                => Promoted(leftBase, rightBase) is not null || SameEnum(left, right)
                    ? wellKnown(WellKnownTypes.Boolean)
                    : null,
            BinaryOperatorKind.Concatenate => wellKnown(WellKnownTypes.String),
            BinaryOperatorKind.Add when Is(leftBase, WellKnownTypes.String) || Is(rightBase, WellKnownTypes.String)
                => wellKnown(WellKnownTypes.String),
            _ => Promoted(leftBase, rightBase) is { } promoted ? wellKnown(promoted) : null,
        };
    }

    /// <summary>
    /// A unary operator declared on the operand's type answers for itself; one answering the operand's
    /// own type -- every numeric negation and Boolean's <c>!</c> -- keeps the operand as written, so a
    /// lift or a refinement on it survives exactly as it did before any declaration was consulted.
    /// The unsigned types and char have no negation at all, where C# would promote them: Standard declares
    /// none for them, and classic refuses <c>-x</c> over each. Named rather than inferred from a missing
    /// declaration, which a model built from Standard's shells alone would show for every numeric type.
    /// </summary>
    public static IMistType? UnaryResultType(UnaryOperatorKind op, IMistType operand)
    {
        if(SelectedUnary(op, operand) is { } candidate)
        {
            return candidate.ReturnType.FullyQualifiedName == Underlying(operand).FullyQualifiedName
                ? operand
                : candidate.ReturnType;
        }

        return op == UnaryOperatorKind.Negate && Array.Exists(Unnegatable, type => Is(Underlying(operand), type)) ? null : operand;
    }

    /// <summary>
    /// The declared operator a binary expression selects, when it is one: null where the result comes
    /// from promotion or from a rule about the operands, which no declaration answers for.
    /// </summary>
    public static Operator? Selected(BinaryOperatorKind op, IMistType left, IMistType right)
    {
        return IsIndeterminate(left) || IsIndeterminate(right)
            || left is UnknownType || right is UnknownType || left is UnionType || right is UnionType
            ? null
            : Declared(op, left, right);
    }

    public static Operator? SelectedUnary(UnaryOperatorKind op, IMistType operand)
    {
        var kind = op switch
        {
            UnaryOperatorKind.Negate => OperatorKind.Minus,
            UnaryOperatorKind.LogicalNot => OperatorKind.Exclamation,
            _ => (OperatorKind?)null,
        };

        if(kind is null || IsIndeterminate(operand))
        {
            return null;
        }

        return Operators(operand).FirstOrDefault(candidate =>
            candidate.Kind == kind.Value
            && candidate.ParametersList.Count == 1
            && Accepts(candidate.ParametersList[0].Type, operand));
    }

    private static Operator? Declared(BinaryOperatorKind op, IMistType left, IMistType right)
    {
        var kind = op switch
        {
            BinaryOperatorKind.Add => OperatorKind.Plus,
            BinaryOperatorKind.Subtract => OperatorKind.Minus,
            BinaryOperatorKind.Multiply => OperatorKind.Star,
            BinaryOperatorKind.Divide => OperatorKind.Slash,
            BinaryOperatorKind.Modulo => OperatorKind.Modulo,
            BinaryOperatorKind.Equal => OperatorKind.EqualEqual,
            BinaryOperatorKind.NotEqual => OperatorKind.NotEqual,
            BinaryOperatorKind.LessThan => OperatorKind.LessThan,
            BinaryOperatorKind.GreaterThan => OperatorKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => OperatorKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => OperatorKind.GreaterThanOrEqual,
            _ => (OperatorKind?)null,
        };

        if(kind is null)
        {
            return null;
        }

        foreach(var candidate in Operators(left).Concat(Operators(right)))
        {
            if(candidate.Kind == kind.Value
                && candidate.ParametersList.Count == 2
                && Accepts(candidate.ParametersList[0].Type, left)
                && Accepts(candidate.ParametersList[1].Type, right))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<Operator> Operators(IMistType type)
    {
        for(var current = (Underlying(type) is ConstructedType constructed ? constructed.Definition : Underlying(type)) as MemberType;
            current is not null;
            current = (current as ConcreteType)?.BaseType as MemberType)
        {
            foreach(var declared in current.OperatorsList)
            {
                yield return declared;
            }
        }
    }

    private static bool Accepts(IMistType parameter, IMistType argument)
    {
        return parameter is TypeParameter
            || ConversionClassifier.Classify(argument, parameter) is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified;
    }

    private static IMistType Underlying(IMistType type)
    {
        return type switch
        {
            RefinedType refined => Underlying(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Underlying(aliased),
            LiftedType lifted => Underlying(lifted.Type),
            UnknownValueType value => Underlying(value.Domain),
            _ => type,
        };
    }

    private static bool SameForeignOrAnonymous(IMistType left, IMistType right)
    {
        var leftDefinition = Definition(Underlying(left));
        var rightDefinition = Definition(Underlying(right));

        if(leftDefinition.FullyQualifiedName != rightDefinition.FullyQualifiedName)
        {
            return false;
        }

        return leftDefinition is AnonymousType or ConcreteType { IsIntrinsic: true } or DeclaredType { Assembly.HasMistSource: false };
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static bool SameEnum(IMistType left, IMistType right)
    {
        return Underlying(left) is EnumType leftEnum && Underlying(right) is EnumType rightEnum
            && leftEnum.FullyQualifiedName == rightEnum.FullyQualifiedName;
    }

    private static bool IsIndeterminate(IMistType type)
    {
        return type is TypeParameter or ErrorType or NotProjectedType or MethodGroupType;
    }

    private static readonly WellKnownType[] NumericPromotionOrder =
    [
        WellKnownTypes.Decimal, WellKnownTypes.Double, WellKnownTypes.Single,
        WellKnownTypes.UInt64, WellKnownTypes.Int64, WellKnownTypes.UInt32,
    ];

    private static readonly WellKnownType[] Unnegatable =
    [
        WellKnownTypes.Byte, WellKnownTypes.UInt16, WellKnownTypes.UInt32, WellKnownTypes.UInt64, WellKnownTypes.Char,
    ];

    private static readonly WellKnownType[] IntegralOperands =
    [
        WellKnownTypes.Int32, WellKnownTypes.Int16, WellKnownTypes.SByte, WellKnownTypes.Byte,
        WellKnownTypes.UInt16, WellKnownTypes.Char,
    ];

    private static WellKnownType? Promoted(IMistType left, IMistType right)
    {
        if(!IsNumeric(left) || !IsNumeric(right))
        {
            return null;
        }

        foreach(var candidate in NumericPromotionOrder)
        {
            if(Is(left, candidate) || Is(right, candidate))
            {
                var other = Is(left, candidate) ? right : left;

                return candidate == WellKnownTypes.UInt32 && IsSigned(other) ? WellKnownTypes.Int64 : candidate;
            }
        }

        return WellKnownTypes.Int32;
    }

    private static bool IsNumeric(IMistType type)
    {
        return NumericPromotionOrder.Concat(IntegralOperands).Any(candidate => Is(type, candidate));
    }

    private static bool IsSigned(IMistType type)
    {
        return Is(type, WellKnownTypes.Int32) || Is(type, WellKnownTypes.Int16) || Is(type, WellKnownTypes.SByte);
    }

    private static bool Is(IMistType type, WellKnownType wellKnown)
    {
        return type.FullyQualifiedName == wellKnown.FullyQualifiedName;
    }
}