using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The collection a value-level <c>params</c> parameter gathers a flat argument list into, and the
/// element each trailing argument must convert to. Decided once, here, so the binder's targets, the
/// argument pairing analysis judges by and the emitted <c>params</c> all hold one element type.
/// <para>
/// The shapes are the ones C# builds a params collection from: an array, a span, one of the five
/// generic collection interfaces, a class or struct enumerable a parameterless constructor and an
/// <c>Add</c> of its element can fill, and an optional over any of these, nullability being an
/// annotation on the collection rather than a different one.
/// </para>
/// </summary>
internal static class ParamsCollection
{
    private static readonly string[] InterfaceNames =
    [
        WellKnownTypes.IEnumerable.FullyQualifiedName,
        "System.Collections.Generic.IReadOnlyCollection<>",
        "System.Collections.Generic.IReadOnlyList<>",
        "System.Collections.Generic.ICollection<>",
        "System.Collections.Generic.IList<>",
    ];

    public static IMistType? ElementOf(IMistType type)
    {
        if(type is TypeAlias { AliasedType: { } aliased })
        {
            return ElementOf(aliased);
        }

        if(OptionalUnion.InnerType(type) is { } present)
        {
            return ElementOf(present);
        }

        if(Indexability.ArrayElement(type) is { } element)
        {
            return element;
        }

        if(type is ConstructedType { TypeArguments.Count: > 0 } constructed
            && (IsSpan(constructed.Definition) || InterfaceNames.Contains(constructed.Definition.FullyQualifiedName)))
        {
            return constructed.TypeArguments[0];
        }

        return EnumerableElement.Of(type) is { } yielded && Fills(type, yielded) ? yielded : null;
    }

    public static bool IsHandedWhole(IMistType argument)
    {
        return EnumerableElement.Of(argument) is not null;
    }

    private static bool IsSpan(IMistType definition)
    {
        return definition.FullyQualifiedName == WellKnownTypes.Span.FullyQualifiedName
            || definition.FullyQualifiedName == WellKnownTypes.ReadOnlySpan.FullyQualifiedName;
    }

    private static bool Fills(IMistType type, IMistType element)
    {
        if(((type as ConstructedType)?.Definition ?? type) is not ConcreteType concrete
            || !IsConstructible(concrete)
            || TypeMemberLookup.Find(concrete, LookupRequest.Member("Add", MemberVisibility.InstanceOnly, null)) is not MethodGroupResult group)
        {
            return false;
        }

        foreach(var add in group.Methods)
        {
            if(add.DeclaringType is not ConcreteType || add.ParametersList is not [{ Modifier.IsRef: false } item])
            {
                continue;
            }

            var path = group.Paths is { } paths && paths.TryGetValue(add, out var reached) ? reached : null;
            var accepts = TypeSubstitution.ForPath(type as ConstructedType, path, interner: null).Apply(item.Type, interner: null);

            if(ReferenceEquals(accepts, element) || ConversionRelation.IsImplicit(element, accepts))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConstructible(ConcreteType concrete)
    {
        return concrete is StructType
            || concrete.ConstructorsList.Count == 0
            || concrete.ConstructorsList.Any(constructor => constructor.ParametersList.Count == 0);
    }
}