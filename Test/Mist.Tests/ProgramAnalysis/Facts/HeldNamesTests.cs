using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class HeldNamesTests
{
    [Fact]
    public void AValueWhoseNameWasWrittenSinceIsSpelledByTheLengthThatStillHoldsIt()
    {
        var model = new FactModel();
        var (bindings, size, i) = Sized(model, written: true, "a");

        var names = HeldNames.Of(bindings, [size, i], []);

        Assert.Equal("a.Length", Assert.Single(names).Value);
        Assert.Equal("fails when a.Length <= i", FailingCase.Describe(new Refuted(new Alternative([Le(0, (size, 1), (i, -1))]), false) { Names = names }));
    }

    [Fact]
    public void AValueWhoseNameWasWrittenSinceAndNoOtherNameHoldsKeepsItsRecordedName()
    {
        var model = new FactModel();
        var n = new LocalVariable("n", model.Expressions.Int);
        var size = model.Value("n");
        var i = model.Value("i");
        var bindings = Bindings.Empty.Set(n, size).Set(new LocalVariable("i", model.Expressions.Int), i).Set(n, model.Value("n"));

        var names = HeldNames.Of(bindings, [size, i], []);

        Assert.Empty(names);
        Assert.Equal("fails when n <= i", FailingCase.Describe(new Refuted(new Alternative([Le(0, (size, 1), (i, -1))]), false) { Names = names }));
    }

    [Fact]
    public void AValueItsRecordedNameStillHoldsKeepsThatNameBesideAnotherHolder()
    {
        var (bindings, size, i) = Sized(new FactModel(), written: false, "a");

        Assert.Empty(HeldNames.Of(bindings, [size, i], ["a.Length"]));
    }

    [Fact]
    public void TheLengthTheUseReadsIsPreferredOverTheFirstBoundHolder()
    {
        var model = new FactModel();
        var (bindings, size, _) = Sized(model, written: true, "a", "b");

        Assert.Equal("b.Length", HeldNames.Of(bindings, [size], ["b.Length"])[size]);
        Assert.Equal("a.Length", HeldNames.Of(bindings, [size], ["i"])[size]);
    }

    [Fact]
    public void ANameNoHolderSpellsIsKeptAfterTheVariableInItIsWritten()
    {
        var model = new FactModel();
        var (bindings, _, i) = Sized(model, written: true, "a");
        var call = model.Value("Zeroes(n).Length");

        Assert.Empty(HeldNames.Of(bindings, [call, i], []));
    }

    private static (Bindings Bindings, Value Size, Value Index) Sized(FactModel model, bool written, params string[] arrays)
    {
        var n = new LocalVariable("n", model.Expressions.Int);
        var size = model.Value("n");
        var i = model.Value("i");
        var bindings = Bindings.Empty.Set(n, size).Set(new LocalVariable("i", model.Expressions.Int), i);
        foreach(var name in arrays)
        {
            var array = new Value(model.Expressions.Sequence(), name, HandBuiltModel.Node());
            bindings = bindings.Set(new LocalVariable(name, array.Type), array)
                .Set(new MemberHolder(array, FactReading.LengthMember(array.Type)), size);
        }

        return (written ? bindings.Set(n, model.Value("n")) : bindings, size, i);
    }
}