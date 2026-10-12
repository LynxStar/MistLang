using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 1: which clauses of a hand-built method gain a key parameter, and how a key is spelled inside a
// body that holds one.
[Trait("Tier", "1")]
public sealed class ContextKeyWitnessTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType text;
    private readonly ClassType owner = HandBuiltModel.Class("Owner");
    private readonly TypeParameter parameter = new("T", 0, new Scope(null), null);

    public ContextKeyWitnessTests()
    {
        text = model.Type("String");
    }

    [Fact]
    public void OnlyAnOpenClauseGainsAParameter_NamedForItsOrdinal()
    {
        var method = Generic("Get", text, parameter, new ConstructedType(HandBuiltModel.Class("Holder<>"), [parameter]));

        Assert.Equal([1, 2], ContextKeyWitnesses.Ordinals(method));
        Assert.Equal(["System.Type __context_1", "System.Type __context_2"], ContextKeyWitnesses.Parameters(method));
        Assert.Equal(["__context_1", "__context_2"], ContextKeyWitnesses.Names(method));
        Assert.Equal([parameter], ContextKeyWitnesses.Keys(Generic("Read", parameter)));
    }

    [Fact]
    public void AMemberTheTargetRunsWithNoCallWritten_CarriesNoParameter()
    {
        var drop = new Method("Drop", text, [], new MemberModifiers()) { DeclaringType = owner };
        drop.ContextBindingsList.Add(new ContextBinding(parameter, "value"));
        var local = new Method("read", text, [], new MemberModifiers());
        local.ContextBindingsList.Add(new ContextBinding(parameter, "value"));

        Assert.Empty(ContextKeyWitnesses.Ordinals(drop));
        Assert.Empty(ContextKeyWitnesses.Ordinals(local));
        Assert.Empty(ContextKeyWitnesses.Parameters(local));
    }

    [Fact]
    public void AClosedKeyIsSpelledAsItsRuntimeType_AndAnOpenKeyAsTheParameterHoldingIt()
    {
        var optional = new UnionType([text, model.Type("Void")]);
        var method = Generic("Get", parameter);
        var holder = new ConstructedType(HandBuiltModel.Class("Holder<>"), [parameter]);

        Assert.Equal("typeof(MistContext.Optional<string>)", ContextKeyWitnesses.Spell(optional));
        Assert.Equal("typeof(T)", ContextKeyWitnesses.Spell(parameter));
        Assert.Null(ContextKeyWitnesses.Held(parameter));

        using(ContextKeyWitnesses.Entering(method))
        {
            Assert.Equal("__context_0", ContextKeyWitnesses.Held(parameter));
            Assert.Equal("__context_0", ContextKeyWitnesses.Spell(parameter));
            Assert.Equal("typeof(Probe.Holder<T>)", ContextKeyWitnesses.Spell(holder));
            Assert.Equal(["__context_0"], ContextKeyWitnesses.Arguments(method, [parameter]));
            Assert.Equal(["typeof(MistContext.Optional<string>)"], ContextKeyWitnesses.Arguments(method, [optional]));
            Assert.Equal(["typeof(T)"], ContextKeyWitnesses.Erased(method));
        }

        Assert.Null(ContextKeyWitnesses.Held(parameter));
    }

    private Method Generic(string name, params IMistType[] keys)
    {
        var method = new Method(name, text, [], new MemberModifiers()) { DeclaringType = owner };
        method.TypeParametersList.Add(parameter);

        foreach(var key in keys)
        {
            method.ContextBindingsList.Add(new ContextBinding(key, $"value{method.ContextBindingsList.Count}"));
        }

        return method;
    }
}