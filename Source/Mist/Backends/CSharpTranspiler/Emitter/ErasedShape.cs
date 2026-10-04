using System.Globalization;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// What a runtime test must check beyond the C# type it names, because <see cref="LengthErasure"/> drops a
/// concrete length from that spelling: <c>int[3]</c> tests as <c>int[] { Length: 3 }</c>, and a tuple
/// carries its elements' checks as <c>{ Item1: { Length: 3 } }</c>. Without them a test succeeds for every
/// length and binds a value whose type promises one it does not have. Written as property-clause MEMBERS so
/// a pattern with members of its own merges both into the one clause C# allows after a type.
/// <para>
/// A sequence whose ELEMENTS carry an erased length has no such clause: C# has no pattern for "every
/// element", so <see cref="UnstatedElements"/> names it for the caller to refuse rather than emit a test
/// that checks the outer length and promises the inner one.
/// </para>
/// </summary>
internal static class ErasedShape
{
    public static IReadOnlyList<string> Members(IMistType type)
    {
        switch(Unwrapped(type))
        {
            case ConstructedType constructed when LengthErasure.ErasedLength(constructed) is { } length:
                return [$"Length: {length.ToString(CultureInfo.InvariantCulture)}"];

            case ITupleType tuple:
                var elements = TypeNameEmitter.Expand(tuple.ElementTypes);
                var members = new List<string>();

                for(var i = 0; i < elements.Count; i++)
                {
                    if(Subpattern(elements[i]) is { } element)
                    {
                        members.Add($"Item{i + 1}: {element}");
                    }
                }

                return members;

            default:
                return [];
        }
    }

    public static string Tested(IMistType type)
    {
        var spelled = TypeNameEmitter.EmitAsPatternType(type);
        var members = Members(type);

        return members.Count == 0 ? spelled : $"{spelled} {Clause(members)}";
    }

    public static string Clause(IEnumerable<string> members)
    {
        return $"{{ {string.Join(", ", members)} }}";
    }

    public static IMistType? UnstatedElements(IMistType type)
    {
        return Unwrapped(type) switch
        {
            ConstructedType constructed when LengthErasure.IsLengthIndexed(constructed) =>
                Subpattern(constructed.TypeArguments[0]) is null
                    ? UnstatedElements(constructed.TypeArguments[0])
                    : constructed,
            ITupleType tuple => TypeNameEmitter.Expand(tuple.ElementTypes)
                .Select(UnstatedElements)
                .FirstOrDefault(found => found is not null),
            UnionType union when OptionalUnion.InnerType(union) is { } inner => UnstatedElements(inner),
            _ => null,
        };
    }

    private static string? Subpattern(IMistType type)
    {
        if(Unwrapped(type) is UnionType union && OptionalUnion.InnerType(union) is { } inner)
        {
            return Subpattern(inner) is { } present ? $"null or {present}" : null;
        }

        var members = Members(type);

        return members.Count == 0 ? null : Clause(members);
    }

    private static IMistType Unwrapped(IMistType type)
    {
        return TypeNameEmitter.Closed(type) switch
        {
            TypeAlias { AliasedType: { } aliased } => Unwrapped(aliased),
            RefinedType refined => Unwrapped(refined.BaseType),
            var closed => closed,
        };
    }
}