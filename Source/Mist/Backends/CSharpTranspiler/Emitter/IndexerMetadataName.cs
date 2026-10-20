using System.Globalization;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class IndexerMetadataName
{
    public static string Of(MemberType? type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<MemberType>();
        var pending = new Queue<MemberType>();
        if(type is not null)
        {
            pending.Enqueue(type);
        }

        while(pending.TryDequeue(out var current))
        {
            if(!seen.Add(current))
            {
                continue;
            }

            names.UnionWith(current.UnprojectedMemberNames);
            names.UnionWith(current.TypeParametersList.Select(parameter => parameter.Name));
            names.UnionWith(SlotLift.Lifted(current).Select(slot => MistType.StripArity(slot.Name)));
            names.UnionWith(current.NestedTypes.Select(nested => MistType.StripArity(nested.Name)));
            names.UnionWith(current.PropertiesList.Select(property => property.Name));
            names.UnionWith(current.MethodsList.Select(method => method.Name));
            if(current is ConcreteType concrete)
            {
                names.UnionWith(concrete.FieldsList.Select(field => field.Name));
            }

            foreach(var inherited in ConversionRelation.Bases(current))
            {
                if(SlotLift.Definition(Ancestry.Unwrap(inherited)) is MemberType definition)
                {
                    pending.Enqueue(definition);
                }
            }
        }

        if(!names.Contains("Item"))
        {
            return "Item";
        }

        var candidate = "Indexer";
        for(var suffix = 1; names.Contains(candidate); suffix++)
        {
            candidate = "Indexer" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }
}