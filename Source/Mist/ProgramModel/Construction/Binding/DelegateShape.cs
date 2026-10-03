using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The function shape a value is called through: a structural function type's own, or the signature of
/// a CLR delegate's <c>Invoke</c>. Mist treats the two as one type, and a delegate named in source is
/// held as the projected class rather than its shape, so this is where the two spellings are made to
/// agree. A delegate is recognised by descent from <c>System.MulticastDelegate</c>, the one base every
/// CLR delegate has and nothing else does.
/// </summary>
internal static class DelegateShape
{
    private const string BaseName = "System.MulticastDelegate";

    public static IFunctionType? Of(IMistType type)
    {
        if(type is IFunctionType function)
        {
            return function;
        }

        return IsClrDelegate(type) ? InvokeShape(type) : null;
    }

    public static bool SameShape(IFunctionType left, IFunctionType right)
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
        if(InvokeOf(type) is not { } method)
        {
            return null;
        }

        var substitution = TypeSubstitution.ForReceiver(type as ConstructedType);
        return new FunctionType(
            method.ParametersList.ConvertAll(parameter => substitution.Apply(parameter.Type, interner: null)),
            substitution.Apply(method.ReturnType, interner: null));
    }

    public static Method? InvokeOf(IMistType type)
    {
        return IsClrDelegate(type) && Definition(type) is ConcreteType definition
            ? definition.MethodsList.Find(method => method.Name == "Invoke")
            : null;
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static bool Same(IMistType left, IMistType right)
    {
        return left.FullyQualifiedName == right.FullyQualifiedName;
    }
}