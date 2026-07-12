using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR constructors onto a Mist ConcreteType shell.
/// </summary>
internal static class ConstructorProjector
{
    /// <summary>
    /// Projects all public constructors onto the given ConcreteType.
    /// Each constructor's return type is set to the declaring type.
    /// </summary>
    public static void Project(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var constructors = clrType.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach(var ctor in constructors)
        {
            var parameters = ProjectParameters(ctor.GetParameters(), concreteType.Scope, resolver);

            var modifiers = new MemberModifiers
            {
                IsPublic = true,
            };

            var projected = new Constructor(concreteType.Name, concreteType, parameters, modifiers);
            concreteType.ConstructorsList.Add(projected);
        }
    }

    /// <summary>
    /// Projects CLR parameter metadata into Mist Parameter instances.
    /// </summary>
    private static List<Parameter> ProjectParameters(ParameterInfo[] clrParams, Scope scope, AmbientTypeResolver resolver)
    {
        var parameters = new List<Parameter>(clrParams.Length);

        foreach(var param in clrParams)
        {
            var paramType = resolver.Resolve(param.ParameterType, scope);
            parameters.Add(new Parameter(param.Name ?? "", paramType));
        }

        return parameters;
    }
}
