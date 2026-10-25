using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ExtensionContractInferenceTests
{
    [Fact]
    public void UnmarkedBodyThatKeepsPurity_IsCallableThroughTheClosedSignature()
    {
        var fixture = new ExtensionContractFragments();
        var declaration = fixture.Declaration(ExtensionContractFragments.Returning(fixture.Number()));
        var view = fixture.View(declaration);

        Assert.Equal(Promise.All, fixture.Inference().Of(view));
        Assert.Empty(fixture.ThroughCaller(view));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImpureBody_IsRefusedAtItsCaller(bool writes)
    {
        var fixture = new ExtensionContractFragments();
        var declaration = fixture.Declaration(writes ? fixture.Writing(fixture.MutableRead()) : ExtensionContractFragments.Returning(fixture.MutableRead()));

        var finding = Assert.Single(fixture.ThroughCaller(fixture.View(declaration)));

        Assert.Equal("MIST_PA_PURE_SCOPE_CALLS_IMPURE", finding.Number.Value);
        Assert.Contains("Helper", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Caller", finding.Message, StringComparison.Ordinal);
        Assert.Equal(38, finding.Position().Start.Column);
    }

    [Fact]
    public void DeclaredPureBody_IsValidatedAtItsOperationAndTrustedAtItsCaller()
    {
        var fixture = new ExtensionContractFragments();
        var declaration = fixture.Declaration(ExtensionContractFragments.Returning(fixture.MutableRead()), new MemberModifiers { IsPure = true });

        var finding = Assert.Single(fixture.Analyze(declaration));

        Assert.Equal("MIST_PA_PURE_SCOPE_READS_MUTABLE", finding.Number.Value);
        Assert.Equal(24, finding.Position().Start.Column);
        Assert.Empty(fixture.ThroughCaller(fixture.View(declaration)));
    }

    [Fact]
    public void ReadonlyInference_FollowsTheSourceBodyForRootAndTransitiveCalls()
    {
        var fixture = new ExtensionContractFragments();
        var read = fixture.View(fixture.Declaration(ExtensionContractFragments.Returning(fixture.MutableRead())));
        var write = fixture.View(fixture.Declaration(fixture.Writing(fixture.MutableRead())));
        var caller = new Method("Caller", fixture.Integer, [], default) { Body = ExtensionContractFragments.Returning(ExtensionContractFragments.Call(read)) };
        var writer = new Method("Writer", fixture.Integer, [], default) { Body = ExtensionContractFragments.Returning(ExtensionContractFragments.Call(write)) };
        var inference = new ReadonlyInference();

        Assert.True(inference.IsReadonly(read));
        Assert.False(inference.IsReadonly(write));
        Assert.True(inference.IsReadonly(caller));
        Assert.False(inference.IsReadonly(writer));
    }

    [Fact]
    public void ReceiverAndParameterIdentities_StayOnTheDeclarationDuringInference()
    {
        var fixture = new ExtensionContractFragments();
        var parameter = new Parameter("value", fixture.T, new ParameterModifier(false, false, OwnershipKind.Frozen));
        var local = new LocalVariable("value", fixture.T) { Parameter = parameter, Ownership = OwnershipKind.Frozen };
        var body = ExtensionContractFragments.Returning(new LocalReferenceExpression(HandBuiltModel.Node(), local));
        var declaration = fixture.Declaration(body, parameters: [parameter]);
        var own = new TypeParameter("U", 0, new Scope(null), null);
        declaration.TypeParametersList.Add(own);
        var first = fixture.View(declaration, fixture.Integer);
        var second = fixture.View(declaration, fixture.Cell);
        var inference = fixture.Inference();

        Assert.True(inference.Of(first).Pure);
        Assert.True(inference.Of(second).Pure);
        Assert.Equal(inference.Of(declaration), inference.Of(second));
        Assert.Same(parameter, local.Parameter);
        Assert.Same(fixture.T, local.Type);
        Assert.Same(own, second.TypeParametersList[0]);
        Assert.Null(first.Body);
        Assert.Null(second.Body);
    }

    [Fact]
    public void LocalAndLocalReadonlyBodies_UseTheSourceReceiverTier()
    {
        var fixture = new ExtensionContractFragments();
        var field = new Field("Value", fixture.Integer, OwnershipKind.Unrestricted, default);
        var receiverRead = new FieldReferenceExpression(HandBuiltModel.Node(), field);
        var read = fixture.Declaration(ExtensionContractFragments.Returning(receiverRead));
        var write = fixture.Declaration(fixture.Writing(receiverRead));
        var frozen = fixture.Declaration(ExtensionContractFragments.Returning(receiverRead), tier: ThisTier.Frozen);
        var inference = fixture.Inference();

        Assert.Equal(new Promise(true, true, false), inference.Of(fixture.View(read)));
        Assert.Equal(new Promise(false, true, false), inference.Of(fixture.View(write)));
        Assert.True(inference.Of(fixture.View(frozen)).Pure);
        Assert.Equal(inference.Of(read), inference.Of(fixture.View(read, fixture.Cell)));
        Assert.Equal(inference.Of(write), inference.Of(fixture.View(write, fixture.Cell)));
        Assert.Empty(fixture.ThroughCaller(fixture.View(read), new MemberModifiers { IsLocal = true, IsReadonly = true }, OwnershipKind.Owned));
        Assert.Empty(fixture.ThroughCaller(fixture.View(write), new MemberModifiers { IsLocal = true }, OwnershipKind.Owned));
    }

    [Fact]
    public void ClosedContext_RemainsAnAmbientInputAtTheCaller()
    {
        var fixture = new ExtensionContractFragments();
        var declaration = fixture.Declaration(ExtensionContractFragments.Returning(fixture.Number()));
        declaration.ContextBindingsList.Add(new ContextBinding(fixture.T, "ambient"));
        var view = fixture.View(declaration);

        var finding = Assert.Single(fixture.ThroughCaller(view));

        Assert.Equal("MIST_PA_PURE_SCOPE_READS_MUTABLE", finding.Number.Value);
        Assert.Contains("ambient", finding.Message, StringComparison.Ordinal);
        Assert.Contains("int", finding.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.ThroughCaller(view, contexts: [new ContextBinding(fixture.Integer, "provided")]));
        _ = Assert.Single(fixture.ThroughCaller(view, contexts: [new ContextBinding(fixture.Cell, "wrong")]));
    }

    [Fact]
    public void OwnedReturnFromAnOwnedReceiver_CannotManufactureFreshProvenance()
    {
        var fixture = new ExtensionContractFragments();
        var declaration = fixture.Declaration(ExtensionContractFragments.Returning(fixture.Number()), tier: ThisTier.Owned,
            ownership: OwnershipKind.Owned, returns: fixture.Cell);
        var view = fixture.View(declaration);
        var input = new LocalVariable("external", fixture.Cell) { Parameter = new Parameter("external", fixture.Cell) };
        var call = ExtensionContractFragments.Call(view, new LocalReferenceExpression(HandBuiltModel.Node(), input));
        var root = ExtensionContractFragments.Returning(call);
        var origins = new Origins(root, null, ScopeKind.Pure);

        Assert.Equal(Origin.External, origins.Of(call));
        Assert.Equal(Origin.Own, origins.Of(ExtensionContractFragments.Call(view,
            new NewExpression(HandBuiltModel.Node(), fixture.Cell, [], null, [], []))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveSpecializedCalls_ConvergeAcrossRepeatedViews(bool impure)
    {
        var fixture = new ExtensionContractFragments();
        var body = HandBuiltModel.Block();
        var declaration = fixture.Declaration(body);
        var first = fixture.View(declaration);
        var second = fixture.View(declaration, fixture.Cell);
        body.Statements = [new ExpressionStatement(ExtensionContractFragments.Call(second))];
        if(impure)
        {
            body.Statements = [.. body.Statements, new ReturnStatement(fixture.MutableRead(), fixture.Integer)];
        }

        var inference = fixture.Inference();

        Assert.Equal(!impure, inference.Of(first).Pure);
        Assert.Equal(inference.Of(first), inference.Of(second));
        Assert.Equal(inference.Of(first), inference.Of(fixture.View(declaration)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenuineBodilessMembers_GainNoInferredPromises(bool clr)
    {
        var fixture = new ExtensionContractFragments();
        var declaration = fixture.Declaration(null, new MemberModifiers { IsClrProjected = clr, IsIntrinsic = !clr });
        var view = fixture.View(declaration);

        Assert.Equal(Promise.None, fixture.Inference().Of(view));
        Assert.False(new ReadonlyInference().IsReadonly(view));
        _ = Assert.Single(fixture.ThroughCaller(view));
    }

    [Fact]
    public void NamedExtensionReceiver_RemainsTheSourceBindingInEveryView()
    {
        var fixture = new ExtensionContractFragments();
        var receiver = fixture.Block.Receiver!;
        var field = new Field("Value", fixture.Cell, OwnershipKind.Unrestricted, default);
        var read = new MemberAccessExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), receiver),
            field.Name, field.Type, member: new FieldResult(field, fixture.T, []));
        var declaration = fixture.Declaration(ExtensionContractFragments.Returning(read));
        var inference = fixture.Inference();

        Assert.Equal(new Promise(true, false, false), inference.Of(fixture.View(declaration)));
        Assert.Equal(inference.Of(declaration), inference.Of(fixture.View(declaration, fixture.Cell)));
        Assert.Same(receiver, fixture.Block.Receiver);
        Assert.Same(fixture.T, receiver.Type);
    }

    [Fact]
    public void ObjectMemberShadowing_StillPreventsInferringTheExtensionBody()
    {
        var fixture = new ExtensionContractFragments();
        var declaration = new Method("ToString", fixture.Integer, [], default)
        {
            Body = ExtensionContractFragments.Returning(fixture.Number()),
            DeclaringBlock = fixture.Block,
        };
        fixture.Block.MethodsList.Add(declaration);

        Assert.Equal(Promise.None, fixture.Inference().Of(fixture.View(declaration)));
    }
}