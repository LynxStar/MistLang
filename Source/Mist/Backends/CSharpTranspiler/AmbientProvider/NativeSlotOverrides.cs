using System.Reflection;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// C# writes an interface's restatement of an inherited member as a private method named for the interface
/// it restates, <c>Ns.IBase.Member</c>, which is how one is told apart from an ordinary private method. It
/// is read once every type's members are projected, the restated slot living on another interface, and a
/// name and arity that do not pick out exactly one slot record nothing.
/// </summary>
internal static class NativeSlotOverrides
{
    public static void Project(AbstractType derived, Type clrType, AmbientTypeResolver resolver)
    {
        foreach(var method in clrType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var dot = method.Name.LastIndexOf('.');

            if(!method.IsVirtual || dot <= 0)
            {
                continue;
            }

            var owner = Unconstructed(method.Name[..dot]);
            var name = method.Name[(dot + 1)..];
            var arity = method.GetParameters().Length;

            foreach(var inherited in clrType.GetInterfaces().Where(inherited => Spelled(inherited) == owner))
            {
                var resolved = resolver.Resolve(inherited, derived.Scope);

                if(((resolved as ConstructedType)?.Definition ?? resolved) is AbstractType restated
                    && restated.MethodsList.Where(slot => slot.Name == name && slot.ParametersList.Count == arity && !slot.Modifiers.IsStatic).ToList() is [var slot])
                {
                    derived.NativeSlotOverridesList.Add((slot, method.IsAbstract));
                }
            }
        }
    }

    private static string Spelled(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var name = definition.FullName ?? definition.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);

        return (tick < 0 ? name : name[..tick]).Replace('+', '.');
    }

    private static string Unconstructed(string spelled)
    {
        var open = spelled.IndexOf('<', StringComparison.Ordinal);

        return open < 0 ? spelled : spelled[..open];
    }
}
