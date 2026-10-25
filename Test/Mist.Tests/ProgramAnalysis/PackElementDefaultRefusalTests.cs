using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: DefaultabilityAnalyzer over `Pair<Type[2]:TArgs> { TArgs[0] First; TArgs[1] Second; }` with no
// constructor. The declaration is admitted, and a default of a closing is refused where it is needed
// exactly when an element the storage zeroes has none.
[Trait("Tier", "2")]
public sealed class PackElementDefaultRefusalTests
{
    private readonly DeclarationFragments fragment = new();

    private readonly GenericDefaultShapes shapes;

    private readonly ConcreteType pair;

    public PackElementDefaultRefusalTests()
    {
        shapes = new GenericDefaultShapes(fragment.Types);
        pair = shapes.Type("Pair", isStruct: true);
        var pack = Pack(pair, "TArgs");
        _ = Slot(pair, "First", pack.ElementAt(0));
        _ = Slot(pair, "Second", pack.ElementAt(1));
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("default", true)]
    [InlineData("new", false)]
    [InlineData("new", true)]
    public void ADefaultOfAClosing_IsRefusedOnlyWhereAnElementLeavesItNone(string form, bool refused)
    {
        var closed = Construct(pair, Closing(shapes.Integer, refused ? shapes.Connection() : shapes.Integer));
        fragment.Root = Local(closed, form == "default"
            ? new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), closed)
            : new NewExpression(HandBuiltModel.Node(30), closed, [], null, [], []));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(refused ? ["MIST_PA_NOT_DEFAULTABLE"] : [], findings.Select(finding => finding.Number.Value));
    }

    [Theory]
    [InlineData("intrinsic", null)]
    [InlineData("connection", "MIST_PA_FIELD_HAS_NO_DEFAULT")]
    [InlineData("holders-own-pack", null)]
    public void ASlotOfAClosing_IsRefusedAtTheHolderOnlyWhereAnElementLeavesItNone(string second, string? refusal)
    {
        var holder = shapes.Type("Holder");
        var closing = second == "holders-own-pack"
            ? Closing(new SpreadType(Pack(holder, "TBoth")))
            : Closing(shapes.Integer, second == "connection" ? shapes.Connection() : shapes.Integer);
        _ = Slot(holder, "Inner", Construct(pair, closing));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(refusal is null ? [] : [refusal], findings.Select(finding => finding.Number.Value));
    }

    private static TypeParameter Pack(ConcreteType owner, string name)
    {
        var pack = Parameter(owner, name);
        pack.PackKind = TypePackKind.Unbracketed;
        pack.PackLength = 2;

        return pack;
    }

    private static LiftedType Closing(params IMistType[] elements)
    {
        return new LiftedType(HandBuiltModel.Class("Type"), new TypePackValue(elements));
    }

    private static Block Local(IMistType type, Expression value)
    {
        return HandBuiltModel.Block(new LocalDeclarationStatement(new LocalVariable("made", type), value));
    }
}