using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A C# class or struct never inherits an interface's members, so a call that selected a projected
/// interface's member on a value of a conforming concrete type is written through that interface, closed as
/// the receiver reaches it. Selection reaches the interface's member only where the type declares none of
/// its own, which is also when C# runs the most specific native default.
/// </summary>
internal static class NativeDefaultCall
{
    public static IMistType? ReceiverType(IMistType receiver, Method? selected)
    {
        if(selected is not { Modifiers.IsStatic: false, DeclaringType: AbstractType { Assembly.Kind: MistAssemblyKind.Ambient } owner })
        {
            return null;
        }

        var start = OptionalUnion.InnerType(receiver) ?? receiver;

        if(Definition(start) is not ConcreteType)
        {
            return null;
        }

        var pending = new Queue<IMistType>([start]);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while(pending.TryDequeue(out var current))
        {
            if(ReferenceEquals(Definition(current), owner))
            {
                return TypeNameEmitter.Closed(current);
            }

            if(seen.Add(current.FullyQualifiedName))
            {
                foreach(var reached in ConversionRelation.Bases(current))
                {
                    pending.Enqueue(reached);
                }
            }
        }

        return null;
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }
}