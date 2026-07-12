using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects base types and implemented interfaces from CLR types onto their
/// Mist shells. Operates after shell creation so all target types are available
/// in the resolver.
/// </summary>
internal static class BaseTypeProjector
{
    /// <summary>
    /// Projects the base type (for classes) and implemented interfaces (for all member types)
    /// from CLR metadata onto the Mist shell.
    /// </summary>
    public static void Project(MistType shell, Type clrType, AmbientTypeResolver resolver)
    {
        if(shell is ConcreteType concreteType)
        {
            ProjectBaseType(concreteType, clrType, resolver);
        }

        if(shell is MemberType memberType)
        {
            ProjectInterfaces(memberType, clrType, resolver);
        }
    }

    /// <summary>
    /// Sets the base type on a ConcreteType (class or struct).
    /// Skips System.Object (implicit base for all classes) and System.ValueType
    /// (implicit base for all structs).
    /// </summary>
    private static void ProjectBaseType(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var baseClrType = clrType.BaseType;
        if(baseClrType is null)
        {
            return;
        }

        concreteType.BaseType = resolver.Resolve(baseClrType, concreteType.Scope);
    }

    /// <summary>
    /// Projects directly implemented/declared interfaces onto the MemberType's
    /// BaseAbstractTypes list.
    /// </summary>
    private static void ProjectInterfaces(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var interfaces = clrType.GetInterfaces();
        foreach(var iface in interfaces)
        {
            // Only project interfaces directly declared on this type, not inherited ones.
            if(!IsDirectlyImplemented(clrType, iface))
            {
                continue;
            }

            var resolved = resolver.Resolve(iface, memberType.Scope);
            memberType.BaseAbstractTypes.Add(resolved);
        }
    }

    /// <summary>
    /// Determines whether an interface is directly declared on a type
    /// (vs inherited from a base type or base interface).
    /// </summary>
    private static bool IsDirectlyImplemented(Type type, Type iface)
    {
        // If the base type also implements this interface, it's inherited, not direct.
        var baseType = type.BaseType;
        if(baseType is not null)
        {
            var baseInterfaces = baseType.GetInterfaces();
            foreach(var baseIface in baseInterfaces)
            {
                if(baseIface == iface)
                {
                    return false;
                }
            }
        }

        // For interfaces, check if any base interface also declares it.
        if(type.IsInterface)
        {
            var baseInterfaces = type.GetInterfaces();
            foreach(var baseIface in baseInterfaces)
            {
                var baseBaseInterfaces = baseIface.GetInterfaces();
                foreach(var transitive in baseBaseInterfaces)
                {
                    if(transitive == iface)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

}