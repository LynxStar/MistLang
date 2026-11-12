using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A type as it leaves the body that made its captures. That body runs many times and each run makes
/// a new value, so a type the binder infers for the body's result cannot hold one: the slot is
/// <c>unknown</c> there, which is what the same creation was before a slot took a runtime value.
/// </summary>
internal static class CaptureScope
{
    public static IMistType Leaving(IMistType type, object body, TypeInterner interner)
    {
        var made = BodyWalker.DescendantsAndSelf(body).SelectMany(SlotCapture.Of).ToHashSet<SlotCapture>(ReferenceEqualityComparer.Instance);

        return made.Count == 0 ? type : Without(type, made, interner);
    }

    private static IMistType Without(IMistType type, HashSet<SlotCapture> made, TypeInterner interner)
    {
        IMistType[] each(IReadOnlyList<IMistType> parts)
        {
            return [.. parts.Select(part => Without(part, made, interner))];
        }

        if(!Mentions(type, made))
        {
            return type;
        }

        return type switch
        {
            CapturedValue captured => interner.GetOrCreateUnknownValue(captured.Domain),
            ConstructedType constructed => interner.GetOrCreateConstructedType(
                constructed.Definition, each(constructed.TypeArguments),
                constructed.Enclosing is { } enclosing ? (ConstructedType)Without(enclosing, made, interner) : null),
            UnionType union => interner.GetOrCreateUnionType(each(union.Constituents)),
            TupleType tuple => interner.GetOrCreateTupleType(each(tuple.ElementTypes)),
            NamedTupleType named => new NamedTupleType((TupleType)Without(named.Underlying, made, interner), named.ElementNames),
            FunctionType function => interner.GetOrCreateFunctionType(each(function.ParameterTypes), Without(function.ReturnType, made, interner), function.IsPure),
            _ => type,
        };
    }

    private static bool Mentions(IMistType type, HashSet<SlotCapture> made)
    {
        return type switch
        {
            CapturedValue captured => made.Contains(captured.Capture),
            ConstructedType constructed => constructed.TypeArguments.Any(argument => Mentions(argument, made))
                || (constructed.Enclosing is { } enclosing && Mentions(enclosing, made)),
            UnionType union => union.Constituents.Any(constituent => Mentions(constituent, made)),
            TupleType tuple => tuple.ElementTypes.Any(element => Mentions(element, made)),
            NamedTupleType named => Mentions(named.Underlying, made),
            FunctionType function => function.ParameterTypes.Any(parameter => Mentions(parameter, made)) || Mentions(function.ReturnType, made),
            _ => false,
        };
    }
}