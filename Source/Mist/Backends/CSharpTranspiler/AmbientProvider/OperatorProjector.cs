using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class OperatorProjector
{
    /// <summary>
    /// Only operators with a Mist representation are listed.
    /// </summary>
    private static readonly Dictionary<string, OperatorKind> OperatorNameMap = new()
    {
        ["op_Addition"] = OperatorKind.Plus,
        ["op_Subtraction"] = OperatorKind.Minus,
        ["op_Multiply"] = OperatorKind.Star,
        ["op_Division"] = OperatorKind.Slash,
        ["op_Modulus"] = OperatorKind.Modulo,
        ["op_Equality"] = OperatorKind.EqualEqual,
        ["op_Inequality"] = OperatorKind.NotEqual,
        ["op_LessThan"] = OperatorKind.LessThan,
        ["op_GreaterThan"] = OperatorKind.GreaterThan,
        ["op_LessThanOrEqual"] = OperatorKind.LessThanOrEqual,
        ["op_GreaterThanOrEqual"] = OperatorKind.GreaterThanOrEqual,
        ["op_LogicalNot"] = OperatorKind.Exclamation,
        ["op_UnaryNegation"] = OperatorKind.Minus,
        ["op_UnaryPlus"] = OperatorKind.Plus,
    };

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var method in methods)
        {
            if(!method.IsSpecialName)
            {
                continue;
            }

            if(method.Name is "op_Implicit" or "op_Explicit")
            {
                ProjectCastOperator(method, memberType, resolver);
            }
            else if(OperatorNameMap.TryGetValue(method.Name, out var kind))
            {
                ProjectOperator(method, kind, memberType, resolver);
            }
        }
    }

    private static void ProjectOperator(MethodInfo method, OperatorKind kind, MemberType memberType, AmbientTypeResolver resolver)
    {
        var returnType = resolver.Resolve(
            method.ReturnType, memberType.Scope, TupleElementNames.Of(method.ReturnParameter, method.ReturnType));
        if(returnType is ErrorType or NotProjectedType)
        {
            return;
        }

        var parameters = ParameterProjector.Project(method.GetParameters(), memberType.Scope, resolver);
        if(parameters is null)
        {
            return;
        }

        var modifiers = new MemberModifiers
        {
            Accessibility = ClrAccessibilityMap.FromMethod(method),
            IsStatic = true,
        };

        var op = new Operator(kind, returnType, parameters, modifiers);
        AttributeProjector.Project(method, op.AttributesList, memberType.Scope, resolver);
        memberType.OperatorsList.Add(op);
    }

    private static void ProjectCastOperator(MethodInfo method, MemberType memberType, AmbientTypeResolver resolver)
    {
        if(memberType is not ConcreteType concreteType)
        {
            return;
        }

        var targetType = resolver.Resolve(
            method.ReturnType, memberType.Scope, TupleElementNames.Of(method.ReturnParameter, method.ReturnType));
        if(targetType is ErrorType or NotProjectedType)
        {
            return;
        }

        var clrParameters = method.GetParameters();
        if(clrParameters.Length != 1)
        {
            return;
        }

        var parameters = ParameterProjector.Project(clrParameters, memberType.Scope, resolver);
        if(parameters is null)
        {
            return;
        }

        var isImplicit = method.Name == "op_Implicit";
        var modifiers = new MemberModifiers
        {
            Accessibility = ClrAccessibilityMap.FromMethod(method),
            IsStatic = true,
        };

        var castOperator = new CastOperator(targetType, parameters[0], isImplicit, modifiers);
        AttributeProjector.Project(method, castOperator.AttributesList, memberType.Scope, resolver);
        concreteType.CastOperatorsList.Add(castOperator);
    }
}