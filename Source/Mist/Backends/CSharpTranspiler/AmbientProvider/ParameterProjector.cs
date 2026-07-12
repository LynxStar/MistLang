using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR method/constructor/indexer parameters into Mist Parameter instances.
/// Shared by all member projectors to ensure consistent filtering (pointer types,
/// unresolvable types).
/// </summary>
internal static class ParameterProjector
{
    /// <summary>
    /// Projects CLR parameters into Mist Parameter instances.
    /// Returns null if any parameter has a pointer type or resolves to an error/not-projected type,
    /// indicating the owning member should be skipped entirely.
    /// </summary>
    public static List<Parameter>? Project(ParameterInfo[] clrParams, Scope scope, AmbientTypeResolver resolver)
    {
        var parameters = new List<Parameter>(clrParams.Length);

        foreach(var param in clrParams)
        {
            if(HasPointerType(param.ParameterType))
            {
                return null;
            }

            var paramType = resolver.Resolve(param.ParameterType, scope);
            if(paramType is ErrorType or NotProjectedType)
            {
                return null;
            }

            parameters.Add(new Parameter(param.Name ?? "", paramType));
        }

        return parameters;
    }

    /// <summary>
    /// Checks whether a CLR type involves pointers (unsafe types that cannot
    /// be represented in Mist).
    /// </summary>
    private static bool HasPointerType(Type type)
    {
        return type.IsPointer || (type.IsByRef && type.GetElementType()!.IsPointer);
    }
}