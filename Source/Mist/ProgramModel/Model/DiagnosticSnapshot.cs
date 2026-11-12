using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

internal static class DiagnosticSnapshot
{
    public static TypeValue Type(IMistType type)
    {
        return Type(type, TypeDisplay.Mist(type));
    }

    /// <summary>
    /// A type that holds a <see cref="CapturedValue"/>, or that is a slot's argument standing alone, is
    /// spelled by <see cref="TypeDisplay"/> whatever spelling the caller chose: a structural name
    /// interpolates the capture's own, which carries a position no reader wrote, and a bare value is
    /// the type of a slot read, which a program sees as the slot's domain.
    /// </summary>
    public static TypeValue Type(IMistType type, string spelling)
    {
        return new TypeValue(TypeIdentity(type), IsValue(type) || HoldsCapture(type) ? TypeDisplay.Mist(type) : spelling);
    }

    private static bool IsValue(IMistType type)
    {
        return type is CapturedValue or UnknownValueType or LiftedType { Value: not TypePackValue };
    }

    private static bool HoldsCapture(IMistType type)
    {
        return type switch
        {
            CapturedValue => true,
            ConstructedType constructed => constructed.TypeArguments.Any(HoldsCapture)
                || (constructed.Enclosing is { } enclosing && HoldsCapture(enclosing)),
            UnionType union => union.Constituents.Any(HoldsCapture),
            ITupleType tuple => tuple.ElementTypes.Any(HoldsCapture),
            FunctionType function => function.ParameterTypes.Any(HoldsCapture) || HoldsCapture(function.ReturnType),
            _ => false,
        };
    }

    public static SymbolValue Symbol(IOverload member, string spelling)
    {
        var owner = member.DeclaringType is { } type ? TypeIdentity(type) : "<local>";
        var name = member switch
        {
            CastOperator cast => $"{cast.Name}->{TypeIdentity(cast.TargetType)}",
            Method method => method.Name,
            _ => member.GetType().Name,
        };
        var parameters = string.Join(",", member.ParametersList.Select(parameter => $"{parameter.Modifier}:{TypeIdentity(parameter.Type)}"));
        return new SymbolValue($"{owner}.{name}`{member.TypeParametersList.Count}({parameters})", spelling);
    }

    public static SymbolValue Symbol(Field member, string spelling)
    {
        var owner = member.DeclaringType is { } type ? TypeIdentity(type) : "<local>";
        return new SymbolValue($"{owner}.field:{member.Name}", spelling);
    }

    public static SymbolValue Symbol(Property member, string spelling)
    {
        var owner = member.DeclaringType is { } type ? TypeIdentity(type) : "<local>";
        return new SymbolValue($"{owner}.property:{member.Name}", spelling);
    }

    private static string TypeIdentity(IMistType type, Method? signatureOwner = null)
    {
        if(type is ConstructedType constructed)
        {
            var arguments = string.Join(",", constructed.TypeArguments.Select(argument => TypeIdentity(argument, signatureOwner)));
            var enclosing = constructed.Enclosing is { } parent ? TypeIdentity(parent, signatureOwner) + "." : "";
            return $"{enclosing}{TypeIdentity(constructed.Definition, signatureOwner)}<{arguments}>";
        }

        if(type is UnionType union)
        {
            return "union(" + string.Join(",", union.Constituents.Select(constituent => TypeIdentity(constituent, signatureOwner))) + ")";
        }

        if(type is ITupleType tuple)
        {
            return "tuple(" + string.Join(",", tuple.ElementTypes.Select(element => TypeIdentity(element, signatureOwner))) + ")";
        }

        if(type is FunctionType function)
        {
            return $"function({string.Join(",", function.ParameterTypes.Select(parameter => TypeIdentity(parameter, signatureOwner)))})->{TypeIdentity(function.ReturnType, signatureOwner)}:pure={function.IsPure}";
        }

        if(type is PackElementType element)
        {
            return $"{TypeIdentity(element.Pack, signatureOwner)}[{element.Index}]";
        }

        if(type is TypeParameter { DeclaringMethod: { } method } parameter)
        {
            if(ReferenceEquals(method, signatureOwner))
            {
                return $"!!{parameter.Ordinal}";
            }

            var owner = method.DeclaringType is { } declaring ? TypeIdentity(declaring) : $"{type.Assembly.ProjectPath ?? type.Assembly.Name}:<local>";
            var signature = string.Join(",", method.ParametersList.Select(item => $"{item.Modifier}:{TypeIdentity(item.Type, method)}"));
            return $"{owner}.{method.Name}`{method.TypeParametersList.Count}({signature}):!!{parameter.Ordinal}:{parameter.Name}";
        }

        if(type is TypeParameter { DeclaringType: { } declaringType } typeParameter)
        {
            return $"{TypeIdentity(declaringType, signatureOwner)}:!{typeParameter.Ordinal}:{typeParameter.Name}";
        }

        return $"{type.Assembly.ProjectPath ?? type.Assembly.Name}:{type.FullyQualifiedName}";
    }
}