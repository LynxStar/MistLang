using System.Diagnostics.CodeAnalysis;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class BaseTypeProjector
{
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

    private static void ProjectBaseType(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var baseClrType = clrType.BaseType;
        if(baseClrType is null)
        {
            return;
        }

        concreteType.BaseType = resolver.Resolve(baseClrType, concreteType.Scope, TupleElementNames.Of(clrType, baseClrType));
    }

    /// <summary>
    /// Projects directly implemented/declared interfaces onto the MemberType's
    /// BaseAbstractTypes list.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
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

            var resolved = resolver.Resolve(iface, memberType.Scope, resolver.TupleNames.OfInterface(clrType, iface));
            memberType.BaseAbstractTypes.Add(resolved);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = MetadataOnlyReflection.Justification)]
    private static bool IsDirectlyImplemented(Type type, Type iface)
    {
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