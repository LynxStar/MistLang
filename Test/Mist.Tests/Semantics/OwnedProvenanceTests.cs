// Where an owned destination already gets a value nothing else holds, and where the binder has to clone.
// A conversion running a Clone body (newtypes spec Requirement 10.1) IS that clone, so cloning its result
// again runs a second Clone and allocates twice; a newtype's field-by-field copy shares the operand's
// fields and still needs one.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class OwnedProvenanceTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("clone-through")]
    [InlineData("declared-owned")]
    public void AConversionThatHandsOverAnOwnedValue_IsNotClonedAgainAtAnOwnedDestination(string conversion)
    {
        var types = new Types();
        var cast = types.Cast(conversion);

        Assert.Same(cast, OwnedProvenance.At(OwnershipKind.Owned, cast));
        Assert.Same(cast, Assert.Single(OwnedProvenance.AtParameters([cast], [new Parameter("taken", cast.Type, new ParameterModifier(false, false, OwnershipKind.Owned))])));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("copy")]
    [InlineData("declared")]
    public void AConversionThatHandsOverNoOwnedValue_IsClonedOnceAtAnOwnedDestination(string conversion)
    {
        var types = new Types();
        var cast = types.Cast(conversion);

        Assert.Same(cast, Assert.IsType<CloneExpression>(OwnedProvenance.At(OwnershipKind.Owned, cast)).Source);
        Assert.Same(cast, OwnedProvenance.At(OwnershipKind.Unrestricted, cast));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnImplicitConversionDeclaringAnOwnedResult_IsOwnedProvenanceAsACastOfItIs()
    {
        var types = new Types();
        var called = types.Declared(owned: true);
        var conversion = new ImplicitConversionExpression(HandBuiltModel.Node(), types.Operand(called.SourceParameter.Type), called.TargetType,
            [new ConversionFunction(called, called.SourceParameter.Type, called.TargetType)]);

        Assert.Same(conversion, OwnedProvenance.At(OwnershipKind.Owned, conversion));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("clone-through", true, null)]
    [InlineData("clone-through", false, "MIST_PA_OWNED_SOURCE_NOT_OWNED")]
    [InlineData("declared-owned", true, null)]
    [InlineData("copy", true, "MIST_PA_OWNED_SOURCE_NOT_OWNED")]
    public void AnOwnedBindingInitializedByAConversion_YieldsOwnershipExactlyWhereTheBinderLeftItUncloned(
        string conversion, bool cloneDeclaresOwned, string? expected)
    {
        var types = new Types(cloneDeclaresOwned);
        var cast = types.Cast(conversion);
        var copy = new LocalVariable("copy", cast.Type) { Ownership = OwnershipKind.Owned };
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(copy, cast), new ReturnStatement(null, null));
        var probe = HandBuiltModel.Class("Probe");
        var run = new Method("Run", types.Model.Type("Void"), [types.Source!.Parameter!], default) { DeclaringType = probe, Body = body };
        probe.MethodsList.Add(run);

        var findings = types.Model.Analyze(new OwnershipAnalyzer(), body, [], probe, run);

        Assert.Equal(expected, findings.SingleOrDefault()?.Number.Value);
    }

    /// <summary>
    /// Owned-fields Requirement 2.3: an owned field read through a holder that neither owns nor leases
    /// it is copied into any destination that keeps the value, and left alone where the destination
    /// aliases it -- a lease or a reference -- or where the holder is owned, which is a move instead.
    /// </summary>
    [Theory]
    [Trait("Tier", "1")]
    [InlineData(OwnershipKind.Unrestricted, false, OwnershipKind.Unrestricted, true)]
    [InlineData(OwnershipKind.Owned, false, OwnershipKind.Unrestricted, true)]
    [InlineData(OwnershipKind.Frozen, false, OwnershipKind.Unrestricted, false)]
    [InlineData(OwnershipKind.Borrowed, false, OwnershipKind.Unrestricted, false)]
    [InlineData(OwnershipKind.Unrestricted, true, OwnershipKind.Unrestricted, false)]
    [InlineData(OwnershipKind.Unrestricted, false, OwnershipKind.Owned, false)]
    public void AnOwnedFieldReadThroughASharedHolder_IsClonedWhereTheDestinationKeepsIt(
        OwnershipKind destination, bool byReference, OwnershipKind holder, bool cloned)
    {
        var holders = new Holders();
        var read = holders.Read(holder);

        var bound = OwnedProvenance.At(destination, read, byReference);

        Assert.Equal(cloned, bound is CloneExpression);
        Assert.Same(read, cloned ? ((CloneExpression)bound).Source : bound);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("owned-field", OwnershipKind.Owned)]
    [InlineData("plain-field", OwnershipKind.Unrestricted)]
    [InlineData("owned-local", OwnershipKind.Owned)]
    [InlineData("ref-local", OwnershipKind.Unrestricted)]
    public void AStoreIsReadAtTheTierItsSlotPromises(string slot, OwnershipKind expected)
    {
        Assert.Equal(expected, OwnedProvenance.OwnershipOf(new Holders().Slot(slot)));
    }

    private sealed class Holders
    {
        private readonly ClassType thing = HandBuiltModel.Class("Thing");
        private readonly ClassType holder = HandBuiltModel.Class("Holder");
        private readonly Field item;
        private readonly Field plain;

        public Holders()
        {
            thing.MethodsList.Add(new Method("Clone", thing, [], new MemberModifiers { IsOwned = true }) { DeclaringType = thing });
            item = new Field("Item", thing, OwnershipKind.Owned, default) { DeclaringType = holder };
            plain = new Field("Plain", thing, OwnershipKind.Unrestricted, default) { DeclaringType = holder };
            holder.FieldsList.AddRange([item, plain]);
        }

        public MemberAccessExpression Read(OwnershipKind holding)
        {
            var local = new LocalVariable("h", holder) { Ownership = holding };
            return Access(new LocalReferenceExpression(HandBuiltModel.Node(), local), item);
        }

        public Expression Slot(string slot)
        {
            var h = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("h", holder));

            return slot switch
            {
                "owned-field" => Access(h, item),
                "plain-field" => Access(h, plain),
                "owned-local" => new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("o", thing) { Ownership = OwnershipKind.Owned }),
                _ => new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("r", thing) { IsRef = true, Ownership = OwnershipKind.Owned }),
            };
        }

        private MemberAccessExpression Access(Expression receiver, Field field)
        {
            return new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type, member: new FieldResult(field, holder, []));
        }
    }

    private sealed class Types
    {
        private readonly ClassType node = HandBuiltModel.Class("Node");
        private readonly ClassType copy = HandBuiltModel.Class("Copy");
        private readonly ClassType money = HandBuiltModel.Class("Money");
        private readonly ClassType cash = HandBuiltModel.Class("Cash");
        private readonly Method nodeClone;

        public Types(bool cloneDeclaresOwned = true)
        {
            var modifiers = new MemberModifiers { IsOwned = cloneDeclaresOwned };
            nodeClone = new Method("Clone", node, [], modifiers) { DeclaringType = node };
            node.MethodsList.Add(nodeClone);
            copy.MethodsList.Add(new Method("Clone", copy, [], modifiers) { DeclaringType = copy });
            copy.CloneSource = node;
            copy.CloneSourceType = node;
            money.Modifiers = new MemberModifiers { IsReadonly = true };
            cash.MethodsList.Add(new Method("Clone", cash, [], modifiers) { DeclaringType = cash });
            cash.CloneSource = money;
            cash.CloneSourceType = money;
        }

        public HandBuiltModel Model { get; } = new();

        public LocalVariable? Source { get; private set; }

        public CastExpression Cast(string conversion)
        {
            var called = conversion switch
            {
                "clone-through" => new CastOperator(copy, new Parameter("value", node), false, default) { DeclaringType = copy, ClonesThrough = nodeClone },
                "copy" => new CastOperator(cash, new Parameter("value", money), false, default) { DeclaringType = cash, CopiesClone = true },
                "declared-owned" => Declared(owned: true),
                _ => Declared(owned: false),
            };

            return new CastExpression(HandBuiltModel.Node(), Operand(called.SourceParameter.Type), called.TargetType,
                [new ConversionFunction(called, called.SourceParameter.Type, called.TargetType)], called.TargetType);
        }

        public CastOperator Declared(bool owned)
        {
            var celsius = HandBuiltModel.Class("Celsius");
            return new CastOperator(copy, new Parameter("value", celsius), false, new MemberModifiers { IsStatic = true, IsOwned = owned }) { DeclaringType = celsius };
        }

        public LocalReferenceExpression Operand(IMistType type)
        {
            Source = new LocalVariable("source", type) { Parameter = new Parameter("source", type) };
            return new LocalReferenceExpression(HandBuiltModel.Node(), Source);
        }
    }
}