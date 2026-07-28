using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR custom attributes onto Mist model entities.
/// Resolves attribute classes through the <see cref="AmbientTypeResolver"/> and
/// preserves positional and named metadata arguments. An attribute whose type cannot
/// be resolved is reported by the resolver and omitted, consistent with how the
/// ambient provider handles every other unresolvable type.
/// </summary>
internal static class AttributeProjector
{
    /// <summary>
    /// Projects custom attributes from a CLR member onto the target attribute list.
    /// </summary>
    public static void Project(MemberInfo member, List<AttributeData> target, Scope scope, AmbientTypeResolver resolver)
    {
        ProjectAttributes(member.GetCustomAttributesData(), target, scope, resolver);
    }

    /// <summary>
    /// Projects custom attributes from a CLR parameter, plus the <c>[Out]</c> calling-convention
    /// marker. <c>[Out]</c> is the only convention synthesized here: in/ref/readonly are already
    /// captured by the parameter's ParameterModifier, and out is the one form the modifier cannot
    /// distinguish from a plain ref.
    /// </summary>
    public static void Project(ParameterInfo parameter, List<AttributeData> target, Scope scope, AmbientTypeResolver resolver)
    {
        ProjectAttributes(parameter.GetCustomAttributesData(), target, scope, resolver);
        EnsureParameterAttribute(parameter.IsOut, typeof(System.Runtime.InteropServices.OutAttribute), target, scope, resolver);
    }

    private static void ProjectAttributes(
        IList<CustomAttributeData> attributes,
        List<AttributeData> target,
        Scope scope,
        AmbientTypeResolver resolver)
    {
        foreach(var attribute in attributes)
        {
            var attributeClrType = attribute.AttributeType;
            if(ShouldSkip(attributeClrType))
            {
                continue;
            }

            var resolved = resolver.Resolve(attributeClrType, scope);
            if(resolved is ErrorType or NotProjectedType)
            {
                continue;
            }

            var positionalArguments = attribute.ConstructorArguments
                .Select(ConvertArgument)
                .ToArray();
            var namedArguments = attribute.NamedArguments
                .Select(argument => (argument.MemberName, ConvertArgument(argument.TypedValue)))
                .ToArray();

            target.Add(new AttributeData(resolved, positionalArguments, namedArguments));
        }
    }

    private static object? ConvertArgument(CustomAttributeTypedArgument argument)
    {
        if(argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            return values.Select(ConvertArgument).ToArray();
        }

        return argument.Value;
    }

    private static void EnsureParameterAttribute(
        bool required,
        Type attributeType,
        List<AttributeData> target,
        Scope scope,
        AmbientTypeResolver resolver)
    {
        if(!required || target.Any(attribute => attribute.AttributeType.FullyQualifiedName == attributeType.FullName))
        {
            return;
        }

        var resolved = resolver.Resolve(attributeType, scope);
        if(resolved is ErrorType or NotProjectedType)
        {
            return;
        }

        target.Add(new AttributeData(resolved, [], []));
    }

    private static bool ShouldSkip(Type attributeType)
    {
        var fullName = attributeType.FullName;
        if(fullName is null)
        {
            return true;
        }

        // Calling-convention and compiler-internal attributes are not projected via this generic
        // path. in/ref/readonly are represented by ParameterModifier and out is synthesized
        // explicitly from the CLR Out flag; a readonly struct becomes the IsReadonly modifier; the
        // rest carry no user-visible semantics. In/Out are also surfaced by reflection as
        // pseudo-custom-attributes derived from parameter flags, so they are filtered here to keep
        // them from leaking in uncontrolled.
        return fullName is
            "System.Runtime.CompilerServices.CompilerGeneratedAttribute"
            or "System.Runtime.CompilerServices.NullableAttribute"
            or "System.Runtime.CompilerServices.NullableContextAttribute"
            or "System.Runtime.CompilerServices.IsReadOnlyAttribute"
            or "System.Runtime.CompilerServices.IsByRefLikeAttribute"
            or "System.Runtime.CompilerServices.ExtensionAttribute"
            or "System.Runtime.CompilerServices.TupleElementNamesAttribute"
            or "System.Runtime.CompilerServices.DynamicAttribute"
            or "System.Runtime.CompilerServices.IsUnmanagedAttribute"
            or "System.Diagnostics.CodeAnalysis.NullableAttribute"
            or "System.Runtime.InteropServices.InAttribute"
            or "System.Runtime.InteropServices.OutAttribute";
    }
}