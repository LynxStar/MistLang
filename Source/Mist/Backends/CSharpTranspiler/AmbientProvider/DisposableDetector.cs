using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class DisposableDetector
{
    private const string DisposableFQN = "System.IDisposable";

    // Heritage must be populated for every shell before this walk: a base's empty shell
    // otherwise hides implementations according to projection enumeration order.
    public static void Apply(ConcreteType concreteType)
    {
        if(concreteType.Assembly.HasMistSource
            || concreteType is { HasDrop: true, IsDisposableDrop: false }
            || !ImplementsIDisposable(concreteType))
        {
            return;
        }

        concreteType.HasDrop = true;
        concreteType.IsDisposableDrop = !concreteType.MethodsList.Exists(method =>
            method.Name == "Drop"
            && method.ParametersList.Count == 0
            && method.Modifiers.Accessibility == Accessibility.Public
            && !method.Modifiers.IsStatic);
    }

    private static bool ImplementsIDisposable(ConcreteType concreteType)
    {
        var pending = new Stack<IMistType>();
        var visited = new HashSet<IMistType>(ReferenceEqualityComparer.Instance);
        pending.Push(concreteType);

        while(pending.TryPop(out var type))
        {
            if(!visited.Add(type))
            {
                continue;
            }

            if(type.FullyQualifiedName == DisposableFQN)
            {
                return true;
            }

            if(type is ConstructedType constructed)
            {
                pending.Push(constructed.Definition);
            }

            if(type is ConcreteType { BaseType: { } baseType })
            {
                pending.Push(baseType);
            }

            if(type is MemberType member)
            {
                foreach(var contract in member.BaseAbstractTypes)
                {
                    pending.Push(contract);
                }
            }
        }

        return false;
    }
}