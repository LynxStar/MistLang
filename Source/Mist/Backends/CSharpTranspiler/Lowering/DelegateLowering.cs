using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A value crossing between Mist's structural <c>func</c> and a CLR delegate that names the same
/// signature -- <c>Predicate</c>, <c>Comparison</c>, <c>RequestDelegate</c>.
/// <para>
/// Mist treats the two as one type, holding the CLR delegate by the function shape its <c>Invoke</c>
/// describes, so nothing in the model distinguishes the sides. C# does: a delegate converts to
/// another delegate of identical signature only through a CONSTRUCTION, never implicitly. So the
/// crossing is materialised at the sites the emitter already knows a target type for, exactly as
/// <see cref="RegexLowering"/> materialises its own.
/// </para>
/// </summary>
internal static class DelegateLowering
{
    private const string BaseName = "System.MulticastDelegate";

    public static bool NeedsConstruction(IMistType source, IMistType? target)
    {
        return target is not null
            && IsNominal(source) != IsNominal(target)
            && Signature(source) is { } from
            && Signature(target) is { } to
            && SameShape(from, to)
            ;
    }

    private static bool IsNominal(IMistType type)
    {
        return type is not IFunctionType;
    }

    private static IFunctionType? Signature(IMistType type)
    {
        if(type is IFunctionType function)
        {
            return function;
        }

        return IsClrDelegate(type) ? InvokeShape(type) : null;
    }

    private static bool IsClrDelegate(IMistType type)
    {
        var current = Definition(type) as ConcreteType;

        while(current is not null)
        {
            if(current.BaseType?.FullyQualifiedName == BaseName)
            {
                return true;
            }

            current = current.BaseType as ConcreteType;
        }

        return false;
    }

    private static IFunctionType? InvokeShape(IMistType type)
    {
        if(Definition(type) is not ConcreteType definition)
        {
            return null;
        }

        foreach(var method in definition.MethodsList)
        {
            if(method.Name == "Invoke")
            {
                return new FunctionType(
                    method.ParametersList.ConvertAll(parameter => Closed(parameter.Type, type)),
                    Closed(method.ReturnType, type));
            }
        }

        return null;
    }

    private static IMistType Closed(IMistType declared, IMistType receiver)
    {
        return declared is TypeParameter { Ordinal: var ordinal }
            && receiver is ConstructedType constructed
            && ordinal < constructed.TypeArguments.Count
                ? constructed.TypeArguments[ordinal]
                : declared
                ;
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static bool SameShape(IFunctionType left, IFunctionType right)
    {
        if(left.ParameterTypes.Count != right.ParameterTypes.Count
            || !Same(left.ReturnType, right.ReturnType))
        {
            return false;
        }

        for(var i = 0; i < left.ParameterTypes.Count; i++)
        {
            if(!Same(left.ParameterTypes[i], right.ParameterTypes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(IMistType left, IMistType right)
    {
        return left.FullyQualifiedName == right.FullyQualifiedName;
    }
}