using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Demand carries closed field types, while a generic struct is emitted once over its parameters.
/// A plain generic slot shares class objects and constructs value types, including tuples, using the
/// structural helpers reached by that demand; calling a whole-type Clone would also copy plain objects.
/// </summary>
internal static class GenericStructConstruction
{
    public static IReadOnlyList<OwnedCopySlot> Slots(StructType type, OwnedCopy template)
    {
        var slots = template.Slots.ToDictionary(slot => slot.Storage.Member);
        foreach(var slot in CloneDemand.Constructions(type).SelectMany(copy => copy.Slots))
        {
            var storage = slot.Storage;
            var owned = storage.Field is { } field ? OwnedProvenance.IsOwned(field) : OwnedProvenance.IsOwned(storage.Property!) || type.IsOwned;
            if(!owned && ContainsParameter(TypeNameEmitter.Closed(storage.Type)))
            {
                slots[storage.Member] = new OwnedCopySlot(storage, new OwnedCopy(storage.Type, OwnedCopyKind.Generic, [], []));
            }
        }

        var ordered = new List<OwnedCopySlot>();
        var visited = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);
        for(ConcreteType? current = type; current is not null && visited.Add(current); current = (current.BaseType is ConstructedType parent ? parent.Definition : current.BaseType) as ConcreteType)
        {
            foreach(var storage in InstanceStorage.Of(current))
            {
                if(slots.TryGetValue(storage.Member, out var slot))
                {
                    ordered.Add(slot);
                }
            }
        }

        return ordered;
    }

    private static bool ContainsParameter(IMistType type)
    {
        return type switch
        {
            TypeParameter => true,
            ConstructedType constructed => constructed.TypeArguments.Any(ContainsParameter),
            ITupleType tuple => tuple.ElementTypes.Any(ContainsParameter),
            UnionType union => union.Constituents.Any(ContainsParameter),
            TypeAlias { AliasedType: { } aliased } => ContainsParameter(aliased),
            RefinedType refined => ContainsParameter(refined.BaseType),
            _ => false,
        };
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("""
            private static TValue __ConstructSlot<TValue>(TValue value)
            {
                return (TValue)__ConstructBox(value);
            }

            private static object __ConstructBox(object value)
            {
                if(value is null) return null;
                var type = value.GetType();
                if(!type.IsValueType) return value;
                var copy = type.GetMethod("__CopyOwned", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if(copy is not null)
                {
                    try
                    {
                        return copy.Invoke(value, null);
                    }
                    catch(System.Reflection.TargetInvocationException failure) when(failure.InnerException is { } inner)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
                        throw;
                    }
                }
                if(value is System.Runtime.CompilerServices.ITuple)
                {
                    foreach(var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    {
                        field.SetValue(value, __ConstructBox(field.GetValue(value)));
                    }
                }
                return value;
            }
            """);
    }
}