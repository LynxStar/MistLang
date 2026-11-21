using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The result of a call to a method whose returned array names a parameter in its length
/// (runtime-dependent-values Requirement 8.10): the length is closed with a capture whose initializer is the
/// argument bound to that parameter, the same expression the call passes, so it is evaluated once, as the
/// argument, and the invocation owns the capture as it owns one an inference made.
/// </summary>
internal static class ReturnLengths
{
    public static (IMistType Type, IReadOnlyList<SlotCapture> Captures) Close(
        IMistType returnType, IReadOnlyList<Expression> arguments, Method selected, IReadOnlyList<ExpressionNode> written, BuilderContext context)
    {
        if(!Holds(returnType))
        {
            return (returnType, []);
        }

        var captures = new List<SlotCapture>();
        var made = new Dictionary<ParameterValue, IMistType>(ReferenceEqualityComparer.Instance);
        var closed = Replace(
            returnType,
            held => made.TryGetValue(held, out var once) ? once : made[held] = Captured(held, arguments, selected, written, context, captures),
            context.ProgramModel.TypeInterner);

        return (closed, captures);
    }

    private static IMistType Captured(
        ParameterValue held, IReadOnlyList<Expression> arguments, Method selected, IReadOnlyList<ExpressionNode> written, BuilderContext context, List<SlotCapture> captures)
    {
        var position = selected.ParametersList.FindIndex(parameter => string.Equals(parameter.Name, held.Parameter.Name, StringComparison.Ordinal));
        var unknown = context.ProgramModel.TypeInterner.GetOrCreateUnknownValue(held.Domain);

        if(position < 0 || position >= arguments.Count)
        {
            return unknown;
        }

        var spelled = position < written.Count ? SlotValueBinder.Written(written[position], held.Parameter.Name) : held.Parameter.Name;

        if(context.Resolver.LookupWellKnown(WellKnownTypes.Array) is not IHasTypeParameters { TypeParametersList: [_, { IsDependent: true } length] }
            || SlotValueBinder.Capture(arguments[position], length, spelled, context.ProgramModel.TypeInterner, converted: true) is not { } capture)
        {
            return unknown;
        }

        captures.Add(capture);
        return capture.Value;
    }

    private static bool Holds(IMistType type)
    {
        return type switch
        {
            ParameterValue => true,
            ConstructedType constructed => constructed.TypeArguments.Any(Holds),
            UnionType union => union.Constituents.Any(Holds),
            _ => false,
        };
    }

    private static IMistType Replace(IMistType type, Func<ParameterValue, IMistType> closing, TypeInterner interner)
    {
        return type switch
        {
            ParameterValue held => closing(held),
            ConstructedType constructed when Holds(constructed) => interner.GetOrCreateConstructedType(
                constructed.Definition, [.. constructed.TypeArguments.Select(argument => Replace(argument, closing, interner))], constructed.Enclosing),
            UnionType union when Holds(union) => interner.GetOrCreateUnionType([.. union.Constituents.Select(constituent => Replace(constituent, closing, interner))]),
            _ => type,
        };
    }
}