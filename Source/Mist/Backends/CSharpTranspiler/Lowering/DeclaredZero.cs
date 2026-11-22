using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Whether a type's declared default is the zero C# already writes into bare storage: no initializer
/// and no default block entry that changes a field anywhere in its instance storage, recursively through
/// substituted field types. A field's own default changes nothing, nor does a zero written into an intrinsic
/// field or tuple element: a constant <see cref="DefaultConstant"/> computes with every bit clear. An open type
/// parameter does not hold, its argument's default being unknown here, and neither does recursive
/// storage, which the analyzers refuse before emission.
/// </summary>
internal static class DeclaredZero
{
    public static bool Holds(IMistType type)
    {
        return Holds(TypeNameEmitter.Closed(type), new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
    }

    private static bool Holds(IMistType type, HashSet<IMistType> visiting)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Holds(aliased, visiting),
            ConstructedType { Definition: TypeAlias { AliasedType: { } aliased } } constructed
                => Holds(TypeSubstitution.ForReceiver(constructed).Apply(aliased, interner: null), visiting),
            StructType { IsIntrinsic: true } or StructType { ClrMetadataName: not null } => true,
            ConstructedType { Definition: StructType { ClrMetadataName: not null } } => true,
            ITupleType => StorageHolds(type, visiting),
            StructType declaration => !Declares(declaration) && StorageHolds(type, visiting),
            ConstructedType { Definition: StructType declaration } => !Declares(declaration) && StorageHolds(type, visiting),
            _ => false,
        };
    }

    private static bool Declares(StructType declaration)
    {
        return DefaultSynthesis.Assignments(declaration).Exists(assignment => assignment.Binding is not null
            || (assignment.Value is { } value && !WritesZero(assignment.Storage.Type, value)));
    }

    private static bool WritesZero(IMistType storage, Expression value)
    {
        return value switch
        {
            DefaultExpression { Type: var type } when ReferenceEquals(type, storage) => true,
            TupleLiteralExpression tuple when storage is ITupleType { ElementTypes: var elements } && elements.Count == tuple.Elements.Count
                => elements.Zip(tuple.Elements).All(element => WritesZero(element.First, element.Second)),
            _ when storage is not StructType { IsIntrinsic: true } => false,
            _ => DefaultConstant.Evaluate(value) is { } constant && DefaultConstant.IsZero(constant),
        };
    }

    private static bool StorageHolds(IMistType type, HashSet<IMistType> visiting)
    {
        if(!visiting.Add(type))
        {
            return false;
        }

        try
        {
            var layout = ValueStorage.Describe(type);
            return layout.IsComplete
                && (layout.BaseType is not { } baseType || Holds(baseType, visiting))
                && layout.Fields.All(field => Holds(field.Type, visiting));
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }
}