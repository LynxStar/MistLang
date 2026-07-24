using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR constructors onto a Mist ConcreteType shell.
/// </summary>
internal static class ConstructorProjector
{
    /// <summary>
    /// Projects all public and protected constructors onto the given ConcreteType.
    /// Skips constructors with pointer or unresolvable parameter types.
    /// </summary>
    public static void Project(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var constructors = clrType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach(var ctor in constructors)
        {
            // Only project public and protected constructors.
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

            var projected = new Constructor(concreteType.Name, concreteType, parameters, modifiers);
            AttributeProjector.Project(ctor, projected.AttributesList, concreteType.Scope, resolver);
            concreteType.ConstructorsList.Add(projected);
        }
    }
}