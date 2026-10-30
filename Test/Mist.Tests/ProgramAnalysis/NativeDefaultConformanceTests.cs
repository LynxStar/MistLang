using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// ConformanceAnalyzer over hand-built conformers of a projected interface, `IGreeter { string Name();
// string Greet() => ...; }`, whose Greet carries a native body and no Mist one.
[Trait("Tier", "2")]
public sealed class NativeDefaultConformanceTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("through-a-source-abstract")]
    [InlineData("restated-with-a-body")]
    public void ANativeDefaultTheConformerLeaves_IsAdmitted(string route)
    {
        var fragment = new DeclarationFragments();
        var (greeter, greet) = Greeter(fragment, hasBody: route != "restated-with-a-body");
        var conformer = fragment.Class("Thing");
        _ = DeclarationFragments.Method(conformer, "Name", fragment.Built.Type("String"));

        switch(route)
        {
            case "direct":
                conformer.BaseAbstractTypes.Add(greeter);
                break;
            case "through-a-source-abstract":
                var polite = fragment.Abstract("IPolite");
                polite.BaseAbstractTypes.Add(greeter);
                conformer.BaseAbstractTypes.Add(polite);
                break;
            default:
                var shouting = Native("IShouting");
                shouting.BaseAbstractTypes.Add(greeter);
                shouting.NativeSlotOverridesList.Add((greet, false));
                conformer.BaseAbstractTypes.Add(shouting);
                break;
        }

        Assert.Empty(fragment.Run(new ConformanceAnalyzer()));
    }

    [Fact]
    public void ARequiredSlotBesideANativeDefault_IsStillReported()
    {
        var fragment = new DeclarationFragments();
        var (greeter, _) = Greeter(fragment, hasBody: true);
        var conformer = fragment.Class("Thing");
        conformer.BaseAbstractTypes.Add(greeter);

        var diagnostic = Assert.Single(fragment.Run(new ConformanceAnalyzer()));

        Assert.Equal("MIST0183", diagnostic.Number.Value);
        Assert.Contains("'string Name()'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reabstracted")]
    [InlineData("restated-twice")]
    public void ADefaultTheReachedInterfacesWithdrawOrDispute_IsTheConformersToAnswer(string shape)
    {
        var fragment = new DeclarationFragments();
        var (greeter, greet) = Greeter(fragment, hasBody: true);
        var conformer = fragment.Class("Thing");
        _ = DeclarationFragments.Method(conformer, "Name", fragment.Built.Type("String"));

        foreach(var name in shape == "reabstracted" ? ["IStrict"] : new[] { "ILeft", "IRight" })
        {
            var restating = Native(name);
            restating.BaseAbstractTypes.Add(greeter);
            restating.NativeSlotOverridesList.Add((greet, shape == "reabstracted"));
            conformer.BaseAbstractTypes.Add(restating);
        }

        var diagnostic = Assert.Single(fragment.Run(new ConformanceAnalyzer()));

        Assert.Equal("MIST0183", diagnostic.Number.Value);
        Assert.Contains("'string Greet()'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticNativeBody_AnswersNoInstanceSlot()
    {
        var fragment = new DeclarationFragments();
        var (greeter, _) = Greeter(fragment, hasBody: false);
        greeter.MethodsList.Add(new Method("Greet", fragment.Built.Type("String"), [new Parameter("other", greeter)], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = greeter,
            HasNativeBody = true,
        });
        var conformer = fragment.Class("Thing");
        _ = DeclarationFragments.Method(conformer, "Name", fragment.Built.Type("String"));
        conformer.BaseAbstractTypes.Add(greeter);

        Assert.Contains("'string Greet()'", Assert.Single(fragment.Run(new ConformanceAnalyzer())).Message, StringComparison.Ordinal);
    }

    private static (AbstractType Greeter, Method Greet) Greeter(DeclarationFragments fragment, bool hasBody)
    {
        var text = fragment.Built.Type("String");
        var greeter = Native("IGreeter");
        greeter.MethodsList.Add(new Method("Name", text, [], DeclarationFragments.Public) { DeclaringType = greeter });
        var greet = new Method("Greet", text, [], DeclarationFragments.Public) { DeclaringType = greeter, HasNativeBody = hasBody };
        greeter.MethodsList.Add(greet);

        return (greeter, greet);
    }

    private static AbstractType Native(string name)
    {
        return new AbstractType(name, "Native", new Scope(null), null, null) { Modifiers = DeclarationFragments.Public };
    }
}