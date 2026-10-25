using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: a pack-typed slot, `TResult Apply(params TArgs args)`, judged against a conformer whose base
// list binds the pack (variadics Requirement 22.2), over declarations constructed directly. A case names
// the elements the base list binds and the parameters the conformer's Apply writes, `;` between them.
[Trait("Tier", "2")]
public sealed class ConformancePackSlotTests
{
    [Theory]
    [InlineData("Int32, Int32", "Int32; Int32")]
    [InlineData("String", "String")]
    [InlineData("", "")]
    [InlineData("TRest...", "params TRest")]
    [InlineData("Int32, TRest...", "Int32; params TRest")]
    public void AMemberWritingThePackOutAtItsArity_FillsTheSlot(string bound, string written)
    {
        Assert.Empty(Build(bound, written).Run(new ConformanceAnalyzer()));
    }

    [Theory]
    [InlineData("Int32, Int32", "Int32", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(int, int)")]
    [InlineData("Int32, Int32", "Int32; Int32; Int32", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(int, int)")]
    [InlineData("Int32, Int32", "Int32; String", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(int, int)")]
    [InlineData("Int32, Int32", "Int32; Int32", "String", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(int, int)")]
    [InlineData("Int32, Int32", "(Int32, Int32)", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(int, int)")]
    [InlineData("String", "Int32", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(string)")]
    [InlineData("", "Int32", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply()")]
    [InlineData("Int32, TRest...", "params TRest", "Int32", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "int Apply(int, (TRest...))")]
    [InlineData("Int32, Int32", null, "Int32", "MIST_PA_SLOT_NOT_FILLED", "int Apply(int, int)")]
    [InlineData("TRest...", null, "Int32", "MIST_PA_SLOT_NOT_FILLED", "int Apply((TRest...))")]
    public void AMemberOfAnotherShape_IsRefusedAgainstTheExpandedSignature(
        string bound, string? written, string returns, string expected, string signature)
    {
        var diagnostic = Assert.Single(Build(bound, written, returns).Run(new ConformanceAnalyzer()));

        Assert.Equal(expected, diagnostic.Number.Value);
        Assert.Contains($"slot '{signature}' of 'IApplier'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefinementOnAnExpandedParameter_IsJudgedAgainstThatElement()
    {
        var fragment = Build("Int32, Int32", written: null);
        var integer = fragment.Built.Type("Int32");
        var positive = new RefinedType(integer, HandBuiltModel.Node(), "Positive")
        {
            Predicate = new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThan, HandBuiltModel.Literal(integer, 0L)),
        };
        _ = DeclarationFragments.Method(Conformer(fragment), "Apply", integer, integer, positive);

        Assert.Empty(fragment.Run(new ConformanceAnalyzer()));
        Assert.Equal("MIST_PA_CONSTRAINT_STRENGTHENED", Assert.Single(fragment.Run(new RefinementAnalyzer())).Number.Value);
    }

    private static ClassType Conformer(DeclarationFragments fragment)
    {
        return fragment.Types.OfType<ClassType>().Single();
    }

    private static DeclarationFragments Build(string bound, string? written, string returns = "Int32")
    {
        var fragment = new DeclarationFragments();
        var interner = fragment.Built.Interner;
        var contract = fragment.Abstract("IApplier");
        var pack = DeclarationFragments.Parameter(contract, contract.Scope, "TArgs");
        pack.PackKind = TypePackKind.Unbracketed;
        var result = DeclarationFragments.Parameter(contract, contract.Scope, "TResult");
        var slot = DeclarationFragments.Method(contract, "Apply", result, new TupleType([new SpreadType(pack)]));
        slot.ParametersList[0].IsFlattened = true;

        var concrete = fragment.Class("Thing");
        var rest = DeclarationFragments.Parameter(concrete, concrete.Scope, "TRest");
        rest.PackKind = TypePackKind.Unbracketed;

        IMistType named(string name)
        {
            return name == "TRest..." ? new SpreadType(rest) : fragment.Built.Type(name);
        }

        var elements = Split(bound, ", ").Select(named).ToList();
        concrete.BaseAbstractTypes.Add(interner.GetOrCreateConstructedType(
            contract, [interner.LiftPack(fragment.Built.Type("Type"), new TypePackValue(elements)), fragment.Built.Type("Int32")]));

        if(written is null)
        {
            return fragment;
        }

        var filler = DeclarationFragments.Method(concrete, "Apply", fragment.Built.Type(returns));

        foreach(var parameter in Split(written, "; "))
        {
            filler.ParametersList.Add(parameter switch
            {
                "params TRest" => new Parameter("rest", new TupleType([new SpreadType(rest)])) { IsFlattened = true },
                _ when parameter.StartsWith('(') => new Parameter("pair", new TupleType([.. Split(parameter.Trim('(', ')'), ", ").Select(named)])),
                _ => new Parameter($"p{filler.ParametersList.Count}", named(parameter)),
            });
        }

        return fragment;
    }

    private static string[] Split(string list, string separator)
    {
        return list.Length == 0 ? [] : list.Split(separator);
    }
}