using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ParameterModifier = Mist.ProgramModel.Model.Enums.ParameterModifier;

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

            var clrParameterType = param.ParameterType.IsByRef
                ? param.ParameterType.GetElementType()!
                : param.ParameterType;
            var paramType = resolver.Resolve(clrParameterType, scope);
            if(paramType is ErrorType or NotProjectedType)
            {
                return null;
            }

            var projected = new Parameter(param.Name ?? "", paramType, BuildModifier(param));
            AttributeProjector.Project(param, projected.AttributesList, scope, resolver);
            parameters.Add(projected);
        }

        return parameters;
    }

    /// <summary>
    /// Builds the parameter modifier from CLR metadata. CLR carries no ownership tier, so the
    /// tier is always <see cref="OwnershipKind.Unrestricted"/>. <c>ref</c> sets <c>IsRef</c>;
    /// <c>in</c> (and <c>ref readonly</c>) additionally set <c>IsReadonly</c>. The CLR calling
    /// convention itself (in/out) is preserved separately by the projected attributes.
    /// </summary>
    private static ParameterModifier BuildModifier(ParameterInfo parameter)
    {
        var isRef = parameter.ParameterType.IsByRef;
        var isReadonly = isRef && IsReadonlyByRef(parameter);
        return new ParameterModifier(isReadonly, isRef, OwnershipKind.Unrestricted);
    }

    /// <summary>
    /// Determines whether a by-ref parameter is readonly (a CLR <c>in</c> or <c>ref readonly</c>
    /// parameter), detected via the <c>In</c> metadata flag or the <c>IsReadOnly</c> required
    /// custom modifier.
    /// </summary>
    private static bool IsReadonlyByRef(ParameterInfo parameter)
    {
        if(parameter.IsIn)
        {
            return true;
        }

        return parameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
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