using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR indexers onto a Mist MemberType shell.
/// In CLR, indexers are properties with parameters (typically named "Item").
/// </summary>
internal static class IndexerProjector
{
    /// <summary>
    /// Projects all public and protected indexers (properties with index parameters)
    /// onto the given MemberType.
    /// Skips indexers with pointer or unresolvable parameter types.
    /// </summary>
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var prop in properties)
        {
            var indexParams = prop.GetIndexParameters();
            if(indexParams.Length == 0)
            {
                continue;
            }

            // Only project if at least one accessor is public or protected.
            if(!HasVisibleAccessor(prop))
            {
                continue;
            }

            var resolvedType = resolver.ResolveInbound(
                prop.PropertyType, memberType.Scope, ClrNullability.ResultIsNonNull(prop, prop));
            if(resolvedType is ErrorType or NotProjectedType)
            {
                continue;
            }

            var parameters = ParameterProjector.Project(indexParams, memberType.Scope, resolver);
            if(parameters is null)
            {
                continue;
            }

            var getter = IsVisibleMethod(prop.GetMethod) ? new PropertyAccessor() : null;
            var setter = IsVisibleMethod(prop.SetMethod) ? new PropertyAccessor() : null;

            var accessibility = ClrAccessibilityMap.FromProperty(prop);
            var modifiers = new MemberModifiers
            {
                Accessibility = accessibility,
                IsStatic = (prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic) == true,
                IsClrProjected = true,
                ClrResultIsNonNull = ClrNullability.ResultIsNonNull(prop, prop),
            };

            var projected = new Indexer(resolvedType, parameters, modifiers)
            {
                DeclaringType = memberType,
                Getter = getter,
                Setter = setter,
            };

            AttributeProjector.Project(prop, projected.AttributesList, memberType.Scope, resolver);
            memberType.IndexersList.Add(projected);
        }
    }

    private static bool HasVisibleAccessor(PropertyInfo prop)
    {
        return IsVisibleMethod(prop.GetMethod) || IsVisibleMethod(prop.SetMethod);
    }

    private static bool IsVisibleMethod(MethodInfo? method)
    {
        if(method is null)
        {
            return false;
        }

        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

}