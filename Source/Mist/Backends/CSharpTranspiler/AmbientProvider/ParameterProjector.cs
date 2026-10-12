using System.Reflection;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
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
        return Project(clrParams, scope, resolver, out _);
    }

    public static List<Parameter>? Project(
        ParameterInfo[] clrParams, Scope scope, AmbientTypeResolver resolver, out Type? unresolvable)
    {
        var parameters = new List<Parameter>(clrParams.Length);
        unresolvable = null;

        foreach(var param in clrParams)
        {
            var clrParameterType = param.ParameterType.IsByRef
                ? param.ParameterType.GetElementType()!
                : param.ParameterType;

            if(HasPointerType(param.ParameterType))
            {
                unresolvable = clrParameterType;
                return null;
            }

            var paramType = resolver.Resolve(clrParameterType, scope, TupleElementNames.Of(param, clrParameterType));
            if(paramType is ErrorType or NotProjectedType)
            {
                unresolvable = clrParameterType;
                return null;
            }

            var projected = new Parameter(param.Name ?? "", paramType, BuildModifier(param))
            {
                IsFlattened = IsParams(param) && ParamsCollection.ElementOf(paramType) is not null,
                HasNativeDefault = HasDefault(param),
            };
            AttributeProjector.Project(param, projected.AttributesList, scope, resolver);
            parameters.Add(projected);
        }

        return parameters;
    }

    internal static bool IsParams(ParameterInfo parameter)
    {
        const string paramArray = "System.ParamArrayAttribute";
        const string paramCollection = "System.Runtime.CompilerServices.ParamCollectionAttribute";

        return !parameter.ParameterType.IsByRef
            && parameter.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName is paramArray or paramCollection);
    }

    /// <summary>
    /// Whether the CLR lets a call omit this parameter, the callee then supplying its default: a C#
    /// default value, or a bare <c>[Optional]</c>. A params collection is omissible too, but as an empty
    /// expanded form rather than a default.
    /// </summary>
    internal static bool HasDefault(ParameterInfo parameter)
    {
        return parameter.IsOptional && !IsParams(parameter);
    }

    /// <summary>
    /// Builds the parameter modifier from CLR metadata. CLR carries no ownership tier, so the tier
    /// is always <see cref="OwnershipKind.Unrestricted"/>.
    /// <para>
    /// IsReadonly FOLLOWS THE DIRECTION rather than being decided beside it, so the two cannot
    /// disagree. It used to be read straight off the <c>In</c> metadata flag, which is set by a
    /// mutable <c>[In] ref</c> as readily as by a genuine <c>in</c>, and that made every COM-style
    /// interop parameter look readonly.
    /// </para>
    /// </summary>
    private static ParameterModifier BuildModifier(ParameterInfo parameter)
    {
        var isRef = parameter.ParameterType.IsByRef;
        var direction = DirectionOf(parameter, isRef);

        return new ParameterModifier(
            direction == ByRefDirection.In, isRef, OwnershipKind.Unrestricted, direction);
    }

    /// <summary>
    /// The calling convention a by-ref parameter demands, which the metadata flags alone do not give.
    /// <para>
    /// THE In FLAG IS NOT READONLY-NESS. It is set by a genuine <c>in</c>, by <c>ref readonly</c>,
    /// and equally by <c>[In] ref</c> and <c>[In, Out] ref</c> -- interop annotations on a parameter
    /// that stays an ordinary mutable <c>ref</c>. Reading the flag as readonly marks 43 such COM
    /// parameters in the WindowsDesktop reference pack readonly, and each then takes the wrong
    /// keyword. Only a readonly MARKER settles it: the compiler emits IsReadOnly for <c>in</c> and
    /// RequiresLocation for <c>ref readonly</c>, and neither appears on a mutable ref.
    /// </para>
    /// <para>
    /// Out is tested first and excludes In, because <c>[In, Out] ref</c> sets both flags and is a
    /// <c>ref</c>: an out parameter is the one whose caller value is not live, which that is not.
    /// </para>
    /// </summary>
    private static ByRefDirection DirectionOf(ParameterInfo parameter, bool isRef)
    {
        if(!isRef)
        {
            return ByRefDirection.None;
        }

        if(parameter.IsOut && !parameter.IsIn)
        {
            return ByRefDirection.Out;
        }

        return HasReadonlyMarker(parameter) ? ByRefDirection.In : ByRefDirection.Ref;
    }

    /// <summary>
    /// The two markers that mean a by-ref parameter genuinely cannot be written through:
    /// <c>IsReadOnly</c> for C# <c>in</c>, and <c>RequiresLocation</c> for <c>ref readonly</c>.
    /// Both arrive as required custom modifiers or as attributes depending on the compiler that
    /// produced the assembly, so both surfaces are checked.
    /// </summary>
    internal static bool IsReadonlyRef(ParameterInfo? result)
    {
        return result is { ParameterType.IsByRef: true } && HasReadonlyMarker(result);
    }

    private static bool HasReadonlyMarker(ParameterInfo parameter)
    {
        const string isReadOnly = "System.Runtime.CompilerServices.IsReadOnlyAttribute";
        const string requiresLocation = "System.Runtime.CompilerServices.RequiresLocationAttribute";

        foreach(var modifier in parameter.GetRequiredCustomModifiers())
        {
            if(modifier.FullName is isReadOnly or requiresLocation)
            {
                return true;
            }
        }

        foreach(var attribute in parameter.GetCustomAttributesData())
        {
            if(attribute.AttributeType.FullName is isReadOnly or requiresLocation)
            {
                return true;
            }
        }

        return false;
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