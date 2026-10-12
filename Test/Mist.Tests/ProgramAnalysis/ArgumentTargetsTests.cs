using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1: ArgumentTargets over directly built signatures. A signature is written as its parameters'
// closed types with `params` on the flattened one and `=` after one with a native default, a call as its
// argument types with `...` for a spread
// the binder kept, and an answer as one type per argument with `-` where nothing pairs with it. `List<T>`
// is a hand-built collection class, enumerable with an Add of T, standing for every params collection.
[Trait("Tier", "1")]
public sealed class ArgumentTargetsTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    private readonly ClassType list;

    public ArgumentTargetsTests()
    {
        list = BuildList();
    }

    [Theory]
    [InlineData("Int32, String", "Int32, String", "Int32, String")]
    [InlineData("params Int32[]", "Int32, String, Int32", "Int32, Int32, Int32")]
    [InlineData("params Int32[]", "", "")]
    [InlineData("String, params Int32[]", "String, Int32, String", "String, Int32, Int32")]
    [InlineData("params Int32[]", "Int32[]", "Int32[]")]
    [InlineData("params Int32[]", "String[]", "Int32[]")]
    [InlineData("params Int32[]", "Int32[], Int32[]", "Int32, Int32")]
    [InlineData("params Int32[][]", "Int32[]", "Int32[]")]
    [InlineData("params List<Int32>", "Int32, String, Int32", "Int32, Int32, Int32")]
    [InlineData("params List<Int32>", "", "")]
    [InlineData("String, params List<Int32>", "String, Int32", "String, Int32")]
    [InlineData("params List<Int32>", "List<Int32>", "List<Int32>")]
    [InlineData("params List<Int32>", "Int32[]", "List<Int32>")]
    [InlineData("params List<Int32>", "List<Int32>, List<Int32>", "Int32, Int32")]
    [InlineData("params Int32[]", "List<Int32>", "Int32[]")]
    [InlineData("params Int32[]?", "Int32, String", "Int32, Int32")]
    [InlineData("params Int32[]?", "Int32[]", "Int32[]?")]
    [InlineData("Int32, params (Int32, String), Boolean", "Byte, Byte, Byte, Byte", "Int32, Int32, String, Boolean")]
    [InlineData("params (Int32, String), String[]", "Byte, Byte, Byte", "Int32, String, String[]")]
    [InlineData("params ()", "", "")]
    [InlineData("Int32, params (), Boolean", "Byte, Byte", "Int32, Boolean")]
    [InlineData("Int32, params (Int32, String), Boolean", "Byte, Byte, Byte", "Int32, -, Boolean")]
    [InlineData("Int32, params (TArgs...), Boolean", "Byte, Byte, Byte, Byte", "Int32, -, -, Boolean")]
    [InlineData("Int32, params (TArgs...)", "Byte, ...", "Int32, -")]
    [InlineData("Int32, String=", "Byte", "Int32")]
    [InlineData("Int32, String=, Boolean=", "Byte", "Int32")]
    [InlineData("Int32, String=, Boolean=", "Byte, String", "Int32, String")]
    [InlineData("Int32, String=, Boolean=", "Byte, String, Byte", "Int32, String, Boolean")]
    [InlineData("Int32=, params Int32[]", "", "")]
    [InlineData("String, Int32=, params Int32[]", "String", "String")]
    public void EachArgumentPairsWithTheSlotOrTheElementItLandsIn(string signature, string arguments, string expected)
    {
        var (declared, closed) = Signature(signature);
        var targets = ArgumentTargets.Of(declared, closed, Arguments(arguments), built.Interner);

        Assert.NotNull(targets);
        Assert.Equal(expected, string.Join(", ", targets.Select(target => target is null ? "-" : Name(target))));
    }

    [Theory]
    [InlineData("Int32, String", "Int32")]
    [InlineData("Int32, params Int32[], Boolean", "Int32")]
    [InlineData("Int32, String=", "")]
    [InlineData("Int32=, String", "Int32")]
    public void ACallThatDoesNotFillTheFixedParameters_PairsNothing(string signature, string arguments)
    {
        var (declared, closed) = Signature(signature);

        Assert.Null(ArgumentTargets.Of(declared, closed, Arguments(arguments), built.Interner));
    }

    [Theory]
    [InlineData("Int32, params (Int32, String), Boolean", "Byte, Byte, Byte", "(Byte)", "(Int32, String)", 1, 1)]
    [InlineData("params (Int32, String)", "", "()", "(Int32, String)", 0, 0)]
    [InlineData("Int32, params (TArgs...), Boolean", "Byte, Byte, Byte, Byte", "(Byte, Byte)", "(TArgs...)", 1, 2)]
    [InlineData("params (TArgs...)", "", "()", "(TArgs...)", 0, 0)]
    [InlineData("params (Int32, TArgs...)", "Byte, String", "(Byte, String)", "(Int32, TArgs...)", 0, 2)]
    public void APackSlotThatIsOpenOrHoldsAnotherCount_IsJudgedWhole(
        string signature, string arguments, string from, string to, int first, int count)
    {
        var (declared, closed) = Signature(signature);
        var slot = ArgumentTargets.Unpaired(declared, closed, Arguments(arguments), built.Interner);

        Assert.NotNull(slot);
        Assert.Equal((from, to, first, count), (Name(slot.From), Name(slot.To), slot.First, slot.Count));
    }

    [Theory]
    [InlineData("Int32, String", "Int32, String")]
    [InlineData("params Int32[]", "String")]
    [InlineData("Int32, params (Int32, String), Boolean", "Byte, String, String, Byte")]
    [InlineData("params ()", "")]
    [InlineData("Int32, params (TArgs...)", "Byte, ...")]
    [InlineData("Int32, params (Int32, String), Boolean", "Byte")]
    public void ASlotPairedByElement_OrHoldingASpread_OrNotReached_IsNot(string signature, string arguments)
    {
        var (declared, closed) = Signature(signature);

        Assert.Null(ArgumentTargets.Unpaired(declared, closed, Arguments(arguments), built.Interner));
    }

    [Fact]
    public void AFunctionValuesSlots_PairByPositionWithNoDeclaration()
    {
        var closed = new IMistType[] { built.Type("Int32"), built.Type("String") };

        Assert.Same(closed, ArgumentTargets.Of(declared: null, closed, closed, built.Interner));
        Assert.Null(ArgumentTargets.Of(declared: null, closed, [built.Type("Int32")], built.Interner));
    }

    private (List<Parameter> Declared, List<IMistType> Closed) Signature(string signature)
    {
        var declared = new List<Parameter>();

        foreach(var written in Split(signature))
        {
            var isParams = written.StartsWith("params ", StringComparison.Ordinal);
            var hasDefault = written.EndsWith('=');
            var type = isParams ? written[7..] : hasDefault ? written[..^1] : written;

            declared.Add(new Parameter($"p{declared.Count}", Type(type)) { IsFlattened = isParams, HasNativeDefault = hasDefault });
        }

        return (declared, [.. declared.Select(parameter => parameter.Type)]);
    }

    private List<IMistType?> Arguments(string arguments)
    {
        return [.. Split(arguments).Select(written => written == "..." ? null : Type(written))];
    }

    private IMistType Type(string written)
    {
        if(written.EndsWith('?'))
        {
            return built.Interner.GetOrCreateUnionType([Type(written[..^1]), built.Type("Void")]);
        }

        if(written.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = Type(written[..^2]);

            return built.Interner.GetOrCreateConstructedType(
                built.Type("Array<,>"), [element, built.Interner.GetOrCreateUnknownValue(built.Type("Int32"))]);
        }

        if(written.StartsWith("List<", StringComparison.Ordinal))
        {
            return built.Interner.GetOrCreateConstructedType(list, [Type(written[5..^1])]);
        }

        if(written.StartsWith('('))
        {
            return built.Interner.GetOrCreateTupleType([.. Split(written[1..^1]).Select(Type)]);
        }

        return written == "TArgs..." ? new SpreadType(pack) : built.Type(written);
    }

    private ClassType BuildList()
    {
        var declared = new ClassType("List<>", "Standard", new Scope(null), null, null);
        var element = new TypeParameter("T", 0, declared.Scope, declared);
        declared.TypeParametersList.Add(element);
        declared.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(
            new AbstractType("IEnumerable<>", "Standard", new Scope(null), null, null), [element]));
        declared.MethodsList.Add(new Method("Add", built.Type("Void"), [new Parameter("item", element)], default) { DeclaringType = declared });

        return declared;
    }

    private static string Name(IMistType type)
    {
        return type switch
        {
            ConstructedType { TypeArguments: [var element, _] } => $"{Name(element)}[]",
            ConstructedType { TypeArguments: [var element] } => $"List<{Name(element)}>",
            UnionType union => $"{Name(union.Constituents[0])}?",
            TupleType tuple => $"({string.Join(", ", tuple.ElementTypes.Select(Name))})",
            _ => type.Name,
        };
    }

    private static List<string> Split(string list)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for(var i = 0; i <= list.Length; i++)
        {
            if(i == list.Length || (list[i] == ',' && depth == 0))
            {
                if(i > start)
                {
                    parts.Add(list[start..i].Trim());
                }

                start = i + 1;
            }
            else if(list[i] is '(' or ')')
            {
                depth += list[i] == '(' ? 1 : -1;
            }
        }

        return parts;
    }
}