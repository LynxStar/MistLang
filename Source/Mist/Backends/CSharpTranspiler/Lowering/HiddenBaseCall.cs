using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class HiddenBaseCall
{
    public static IMistType? ReceiverType(IMistType receiver, Method? selected)
    {
        if(selected is not { DeclaringType: ClassType owner }
            || selected.Modifiers.IsIntrinsic || selected.Modifiers.IsClrProjected)
        {
            return null;
        }

        var hidden = false;
        for(var current = OptionalUnion.InnerType(receiver) ?? receiver; current is not null; current = BaseOf(current))
        {
            if(ReferenceEquals(Definition(current), owner))
            {
                return hidden ? TypeNameEmitter.Closed(current) : null;
            }

            if(Definition(current) is ClassType candidate)
            {
                hidden |= candidate.MethodsList.Exists(method => method.Name == selected.Name)
                    || candidate.PropertiesList.Exists(property => property.Name == selected.Name)
                    || candidate.FieldsList.Exists(field => field.Name == selected.Name);
            }
        }

        return null;
    }

    private static IMistType? BaseOf(IMistType type)
    {
        return (type, Definition(type)) switch
        {
            (ConstructedType constructed, ConcreteType { BaseType: { } baseType })
                => TypeSubstitution.ForReceiver(constructed).Apply(baseType, interner: null),
            (_, ConcreteType { BaseType: var baseType }) => baseType,
            _ => null,
        };
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }
}