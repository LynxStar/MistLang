using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR operator overloads and cast operators (implicit/explicit conversions)
/// onto a Mist MemberType or ConcreteType shell. CLR operators are static methods
/// with <see cref="MethodBase.IsSpecialName"/> and names like "op_Addition",
/// "op_Implicit", "op_Explicit".
/// </summary>
internal static class OperatorProjector
{
    /// <summary>
    /// Maps CLR operator method names to Mist <see cref="OperatorKind"/> values.
    /// Only operators that have a Mist representation are included.
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

    /// <summary>
    /// Projects all public operator overloads and cast operators onto the given type.
    /// Regular operators go to <see cref="MemberType.OperatorsList"/>.
    /// Cast operators go to <see cref="ConcreteType.CastOperatorsList"/> (if applicable).
    /// </summary>
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

    /// <summary>
    /// Projects a regular operator (arithmetic, comparison, logical) onto the type.
    /// </summary>
    private static void ProjectOperator(MethodInfo method, OperatorKind kind, MemberType memberType, AmbientTypeResolver resolver)
    {
        var returnType = resolver.Resolve(method.ReturnType, memberType.Scope);
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

    /// <summary>
    /// Projects an implicit or explicit cast operator onto the type.
    /// Cast operators are only stored on <see cref="ConcreteType"/> (classes/structs).
    /// </summary>
    private static void ProjectCastOperator(MethodInfo method, MemberType memberType, AmbientTypeResolver resolver)
    {
        if(memberType is not ConcreteType concreteType)
        {
            return;
        }

        var targetType = resolver.Resolve(method.ReturnType, memberType.Scope);
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