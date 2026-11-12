using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class ConstructorProjector
{
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    public static void Project(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var constructors = clrType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach(var ctor in constructors)
        {
            if(!ctor.IsPublic && !ctor.IsFamily && !ctor.IsFamilyOrAssembly)
            {
                continue;
            }

            var parameters = ParameterProjector.Project(ctor.GetParameters(), concreteType.Scope, resolver);
            if(parameters is null)
            {
                continue;
            }

            var modifiers = new MemberModifiers
            {
                Accessibility = ClrAccessibilityMap.FromMethod(ctor),
            };

            var projected = new Constructor(concreteType.Name, concreteType, parameters, modifiers)
            {
                DeclaringType = concreteType,
            };
            AttributeProjector.Project(ctor, projected.AttributesList, concreteType.Scope, resolver);
            concreteType.ConstructorsList.Add(projected);
        }
    }
}