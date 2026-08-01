using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

internal static class StorageTypeValidation
{
    /// <summary>
    /// Reports a declared storage type that mentions <c>unknown</c>.
    /// </summary>
    /// <param name="description">
    /// How the member is named to the user, already including its kind -- "Field 'Holder.slot'".
    /// Supplied by the caller because only the caller knows which kind of member it is building.
    /// </param>
    public static void Validate(IMistType type, string description, SyntaxNode node, BuilderContext context)
    {
        if(!ContainsUnknown(type))
        {
            return;
        }

        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownAtStoragePosition, node, null, description, TypeDisplay.Mist(type));
    }

    /// <summary>
    /// Whether <paramref name="type"/> mentions <c>unknown</c> anywhere in its type expression.
    /// </summary>
    public static bool ContainsUnknown(IMistType type)
    {
        return type switch
        {
            UnknownType => true,
            ConstructedType constructed => AnyContainsUnknown(constructed.TypeArguments),
            UnionType union => AnyContainsUnknown(union.Constituents),
            NamedTupleType named => AnyContainsUnknown(named.ElementTypes),
            TupleType tuple => AnyContainsUnknown(tuple.ElementTypes),
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