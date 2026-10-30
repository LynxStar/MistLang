using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

internal static class StorageTypeValidation
{
    /// <param name="description">Already includes the member kind -- "Field 'Holder.slot'".</param>
    public static void Validate(IMistType type, string description, SyntaxNode node, BuilderContext context)
    {
        if(!ContainsUnknown(type))
        {
            return;
        }

        context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.UnknownAtStoragePosition, node, null, new TextValue(description), DiagnosticSnapshot.Type(type));
    }

    public static bool ContainsUnknown(IMistType type)
    {
        return type switch
        {
            UnknownType => true,
            ConstructedType constructed => AnyContainsUnknown(constructed.TypeArguments),
            UnionType union => AnyContainsUnknown(union.Constituents),
            NamedTupleType named => AnyContainsUnknown(named.ElementTypes),
            TupleType tuple => AnyContainsUnknown(tuple.ElementTypes),
            // UnknownValueType is also named "unknown", but it is an array's length argument, so
            // matching it would refuse every array field.
            _ => false,
        };
    }

    private static bool AnyContainsUnknown(IReadOnlyList<IMistType> types)
    {
        for(var i = 0; i < types.Count; i++)
        {
            if(ContainsUnknown(types[i]))
            {
                return true;
            }
        }

        return false;
    }
}