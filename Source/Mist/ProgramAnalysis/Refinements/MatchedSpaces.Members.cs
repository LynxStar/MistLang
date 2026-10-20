using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

internal static partial class MatchedSpaces
{
    private static ValueAlgebra<object>? Members(IMistType structure, bool nullable)
    {
        var definition = (structure as ConstructedType)?.Definition ?? structure;

        if(definition is not (ClassType or StructType or AbstractType))
        {
            return null;
        }

        var product = MemberProduct(structure);

        return nullable && IsReference(definition) ? WithNull(product) : product;
    }

    // A dimension is the member the binder resolved, not its name: a member hidden by a derivation or
    // declared again on another type shares the name and nothing else, and the same member reached from
    // the same receiver resolves to the same declaration in every arm.
    private static ValueAlgebra<object> MemberProduct(IMistType structure)
    {
        var boxes = new Boxes([]);
        var dimensions = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        IReadOnlyList<object?[]> everything = [Boxes.Everything];

        return new ValueAlgebra<IReadOnlyList<object?[]>>(
            everything, [],
            boxes.Meet,
            (left, right) => Limited([.. left, .. right]),
            values => boxes.Subtract(everything, values),
            (values, other) => boxes.Subtract(values, other) is { Count: 0 },
            (pattern, self) => pattern switch
            {
                PropertyPattern { IsFailed: false } property => Read(property, structure, boxes, dimensions, self),
                TypeTestPattern test when Tests(test, structure) => MatchedValues.Tested(test, self),
                _ => null,
            }).Erased();
    }

    private static IReadOnlyList<object?[]>? Read(
        PropertyPattern property,
        IMistType structure,
        Boxes boxes,
        Dictionary<object, int> dimensions,
        ValueAlgebra<IReadOnlyList<object?[]>> self)
    {
        var shape = self.Everything;

        if(property.TypeTest is { } test)
        {
            if(!Tests(test, structure) || MatchedValues.Tested(test, self) is not { } tested)
            {
                return null;
            }

            shape = tested;
        }

        var parts = new Dictionary<int, object>();

        foreach(var subpattern in property.Subpatterns)
        {
            if(subpattern.IsMemberFailed || Storage(subpattern.Member) is not { } member)
            {
                return null;
            }

            if(!dimensions.TryGetValue(member, out var dimension))
            {
                dimension = boxes.Add(Element(subpattern.MemberType));
                dimensions[member] = dimension;
            }

            var element = boxes[dimension];

            if(element.Read(subpattern.Pattern) is not { } part)
            {
                return null;
            }

            if(parts.TryGetValue(dimension, out var previous))
            {
                if(element.And(previous, part) is not { } both)
                {
                    return null;
                }

                part = both;
            }

            parts[dimension] = part;

            if(element.IsEmpty(part))
            {
                return [];
            }
        }

        var box = new object?[boxes.Count];

        foreach(var (dimension, part) in parts)
        {
            box[dimension] = part;
        }

        return boxes.Meet(shape, [box]);
    }

    private static object? Storage(LookupResult member)
    {
        return member switch
        {
            PropertyResult { Property: { Modifiers.IsStatic: false } property } => property,
            FieldResult { Field: { Modifiers.IsStatic: false } field } => field,
            _ => null,
        };
    }
}