using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR custom attributes onto Mist model entities.
/// Resolves the attribute class type through the <see cref="AmbientTypeResolver"/>
/// and creates <see cref="AttributeData"/> instances (without argument resolution).
/// Attributes whose type cannot be resolved are silently skipped.
/// </summary>
internal static class AttributeProjector
{
    /// <summary>
    /// Projects custom attributes from a CLR member (type, method, property, field)
    /// onto the target attribute list.
    /// </summary>
    public static void Project(MemberInfo member, List<AttributeData> target, Scope scope, AmbientTypeResolver resolver)
    {
        IList<CustomAttributeData> attributes;
        try
        {
            attributes = member.GetCustomAttributesData();
        }
        catch
        {
            return;
        }

        ProjectAttributes(attributes, target, scope, resolver);
    }

    /// <summary>
    /// Projects custom attributes from a CLR parameter onto the target attribute list.
    /// </summary>
    public static void Project(ParameterInfo parameter, List<AttributeData> target, Scope scope, AmbientTypeResolver resolver)
    {
        IList<CustomAttributeData> attributes;
        try
        {
            attributes = parameter.GetCustomAttributesData();
        }
        catch
        {
            return;
        }

        ProjectAttributes(attributes, target, scope, resolver);
    }

    /// <summary>
    /// Core projection logic: resolves each attribute's type and creates
    /// an <see cref="AttributeData"/> entry. Skips attributes whose type
    /// cannot be resolved (ErrorType, NotProjectedType) or that are
    /// compiler-internal (CompilerGenerated, etc.).
    /// </summary>
    private static void ProjectAttributes(
        IList<CustomAttributeData> attributes,
        List<AttributeData> target,
        Scope scope,
        AmbientTypeResolver resolver)
    {
        foreach(var attr in attributes)
        {
            var attrClrType = attr.AttributeType;

            if(ShouldSkip(attrClrType))
            {
                continue;
            }

            var resolved = resolver.Resolve(attrClrType, scope);
            if(resolved is ErrorType or NotProjectedType)
            {
                continue;
            }

            target.Add(new AttributeData(resolved, attributeNode: null));
        }
    }

    /// <summary>
    /// Determines whether a CLR attribute type should be skipped during projection.
    /// Skips compiler-internal attributes that have no semantic meaning for Mist.
    /// </summary>
    private static bool ShouldSkip(Type attrType)
    {
        var fullName = attrType.FullName;
        if(fullName is null)
        {
            return true;
        }

        // Skip compiler-internal attributes that don't carry user-visible semantics.
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
            or "System.Diagnostics.CodeAnalysis.NullableAttribute";
    }
}