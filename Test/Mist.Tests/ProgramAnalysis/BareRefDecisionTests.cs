using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LendFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class BareRefDecisionTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("owned local", "ref", "owned binding", "b")]
    [InlineData("owned local", "out", "owned binding", "b")]
    [InlineData("owned parameter", "ref", "owned binding", "b")]
    [InlineData("owned field of an owned local", "ref", "owned field", "p.Kept")]
    [InlineData("owned field of an unrestricted local", "ref", "owned field", "p.Kept")]
    [InlineData("owned field of a borrowed parameter", "ref", "owned field", "p.Kept")]
    [InlineData("field of an owned type", "ref", "owned field", "q.In")]
    [InlineData("owned field of this", "ref", "owned field", "Kept")]
    [InlineData("owned local of a struct holding an object", "ref", "owned binding", "s")]
    [InlineData("owned local", "readonly ref", null, null)]
    [InlineData("owned local", "borrowed", null, null)]
    [InlineData("owned local", "owned", null, null)]
    [InlineData("owned local", "", null, null)]
    [InlineData("owned local of a struct holding an int", "ref", null, null)]
    [InlineData("owned local of an int", "ref", null, null)]
    [InlineData("owned local of a string", "ref", null, null)]
    [InlineData("plain field of an owned local", "ref", null, null)]
    [InlineData("unrestricted local", "ref", null, null)]
    [InlineData("inferred local", "ref", null, null)]
    [InlineData("borrowed parameter", "ref", null, null)]
    [InlineData("ref local", "ref", null, null)]
    [InlineData("static field", "ref", null, null)]
    public void OnlyAnOwnedBindingOrOwnedFieldThatCanChangeIsRefusedABareRef(string argument, string parameter, string? kind, string? name)
    {
        var refused = TierAnalyzer.PassedByBareRef(Argument(argument), Tier(parameter), new CloneDerivation([]), root: null);

        Assert.Equal(kind is null ? null : (kind, name!), refused);
    }

    [Theory]
    [InlineData("ref", ThisTier.None)]
    [InlineData("out", ThisTier.None)]
    [InlineData("readonly ref", ThisTier.Owned)]
    [InlineData("", ThisTier.Owned)]
    public void AnInferredLocalPassedByBareRefHoldsNothingAsOwned(string parameter, ThisTier inferred)
    {
        var b = new LocalVariable("b", f.Box) { InfersOwnership = true };
        var pass = new Method("Pass", f.Model.Type("Void"), [new Parameter("x", f.Box, Tier(parameter))], new MemberModifiers { IsStatic = true });
        var set = new Method("Set", f.Model.Type("Void"), [new Parameter("x", f.Box, Tier("borrowed"))], new MemberModifiers { IsStatic = true });
        var block = HandBuiltModel.Block(Declare(b, Fresh(f.Box)), Run(Call(pass, null, 0, Ref(b))), Run(Call(set, null, 0, Ref(b))));

        Assert.Equal(inferred, LocalOwnership.For(block).Of(b));
    }

    private Expression Argument(string argument)
    {
        var owned = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
        return argument switch
        {
            "owned local" => Ref(new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned }),
            "owned parameter" => Ref(Parameter("b", f.Box, Tier("owned"))),
            "owned field of an owned local" => Access(Ref(owned), f.Kept),
            "owned field of an unrestricted local" => Access(Ref(new LocalVariable("p", f.Holder)), f.Kept),
            "owned field of a borrowed parameter" => Access(Ref(Parameter("p", f.Holder, Tier("borrowed"))), f.Kept),
            "field of an owned type" => OfOwnedType(),
            "owned field of this" => new FieldReferenceExpression(HandBuiltModel.Node(), f.Kept),
            "owned local of a struct holding an object" => Ref(new LocalVariable("s", Struct("Held", f.Box)) { Ownership = OwnershipKind.Owned }),
            "owned local of a struct holding an int" => Ref(new LocalVariable("s", Struct("Count", Int32)) { Ownership = OwnershipKind.Owned }),
            "owned local of an int" => Ref(new LocalVariable("n", Int32) { Ownership = OwnershipKind.Owned }),
            "owned local of a string" => Ref(new LocalVariable("s", f.Model.Type("String")) { Ownership = OwnershipKind.Owned }),
            "plain field of an owned local" => Access(Ref(owned), f.In),
            "unrestricted local" => Ref(new LocalVariable("b", f.Box)),
            "inferred local" => Ref(new LocalVariable("b", f.Box) { InfersOwnership = true }),
            "borrowed parameter" => Ref(Parameter("b", f.Box, Tier("borrowed"))),
            "ref local" => Ref(new LocalVariable("r", f.Box) { IsRef = true, Ownership = OwnershipKind.Owned }),
            _ => Static(f.S),
        };
    }

    private Expression OfOwnedType()
    {
        var type = HandBuiltModel.Class("Q");
        type.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        return Access(Ref(new LocalVariable("q", type) { Ownership = OwnershipKind.Owned }), Member(type, "In", f.Box));
    }

    private static StructType Int32 { get; } = new("Int32", "Standard", new Scope(null), null, null);

    private static StructType Struct(string name, IMistType held)
    {
        var structure = new StructType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        structure.FieldsList.Add(new Field("B", held, default, DeclarationFragments.Public) { DeclaringType = structure });
        return structure;
    }
}