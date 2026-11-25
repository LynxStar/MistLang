using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

/// <summary>
/// The intrinsic-or-body contract a callable member is built against (standard-library spec
/// Requirements 5.1-5.4 and 5.15, abstract-type-unification's slots) and the 'with' tier a
/// constructor may not choose (MIST0341). Every refusal is reported once at the declaration and the
/// member is still recorded, so nothing downstream cascades from it.
/// </summary>
[Trait("Tier", "2")]
public sealed class MemberBodyConstructionTests
{
    private const int MemberOffset = 40;

    private readonly BindingFragments f = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIntrinsicMemberOfAnAbstractTypeIsRefusedOnceForThat(bool withBody)
    {
        var method = Build(new AbstractType("Shape", "Probe", f.Types, null, null), Method(intrinsic: true, withBody));

        var diagnostic = AssertRefusedOnce("MIST0324");
        Assert.Equal("The 'intrinsic' modifier is not valid on abstract members.", diagnostic.Message);
        Assert.True(method.Modifiers.IsIntrinsic);
    }

    [Fact]
    public void ABodyOnAnIntrinsicMemberOfAnIntrinsicTypeIsRefused()
    {
        var method = Build(Class(intrinsic: true), Method(intrinsic: true, withBody: true));

        var diagnostic = AssertRefusedOnce("MIST0326");
        Assert.Equal("Intrinsic member 'Compute' must not have a body; use ';' to mark it as backend-provided.", diagnostic.Message);
        Assert.NotNull(method.Body);
    }

    [Fact]
    public void AnIntrinsicMemberOfAnOrdinaryTypeIsRefused()
    {
        _ = Build(Class(intrinsic: false), Method(intrinsic: true, withBody: false));

        var diagnostic = AssertRefusedOnce("MIST0328");
        Assert.Equal("Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type 'Holder' is not intrinsic.", diagnostic.Message);
    }

    [Fact]
    public void ABodiedIntrinsicMemberOfAnOrdinaryTypeIsRefusedForBoth()
    {
        _ = Build(Class(intrinsic: false), Method(intrinsic: true, withBody: true));

        var refusals = f.Diagnostics.ToImmutableArray();
        Assert.Equal(["MIST0326", "MIST0328"], refusals.Select(diagnostic => diagnostic.Number.Value));
        Assert.All(refusals, diagnostic => Assert.Equal(MemberOffset + 1, diagnostic.Position().Start.Column));
    }

    [Fact]
    public void AnOrdinaryMemberWithoutABodyOnAConcreteTypeIsRefused()
    {
        var method = Build(Class(intrinsic: true), Method(intrinsic: false, withBody: false));

        var diagnostic = AssertRefusedOnce("MIST0325");
        Assert.Equal("Method 'Compute' must have a body unless it is declared 'intrinsic' or is on an abstract type.", diagnostic.Message);
        Assert.Null(method.Body);
    }

    [Fact]
    public void AnIntrinsicMemberWithoutABodyInAnIntrinsicTypeIsAdmitted()
    {
        var method = Build(Class(intrinsic: true), Method(intrinsic: true, withBody: false));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.True(method.Modifiers.IsIntrinsic);
        Assert.Null(method.Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOrdinaryMemberOfAnAbstractTypeIsAdmittedAsASlotOrADefault(bool withBody)
    {
        var method = Build(new AbstractType("Shape", "Probe", f.Types, null, null), Method(intrinsic: false, withBody));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(withBody, method.Body is not null);
    }

    [Fact]
    public void TheStandardCatchIntrinsicOnAnExtensionIsAdmitted()
    {
        _ = Build(ExceptionExtension(MistAssemblyKind.Standard), Method(intrinsic: true, withBody: false, name: "Catch"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AUserCatchIntrinsicOnAnExtensionIsRefused()
    {
        _ = Build(ExceptionExtension(MistAssemblyKind.Self), Method(intrinsic: true, withBody: false, name: "Catch"));

        var diagnostic = AssertRefusedOnce("MIST0328");
        Assert.Contains("Type 'extension(self)' is not intrinsic.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("owned")]
    [InlineData("borrowed")]
    [InlineData("frozen")]
    [InlineData("readonly ref")]
    public void AConstructorThatChoosesTheTierOfThisIsRefusedAndStillRecorded(string tier)
    {
        var constructor = BuildConstructor([], new ThisTierWithClauseWithClauseNode { Tier = Tier(tier) });

        _ = AssertRefusedOnce("MIST0341");
        Assert.NotNull(constructor.Body);
    }

    [Fact]
    public void AnAsyncConstructorThatChoosesTheTierOfThisIsRefusedForBoth()
    {
        _ = BuildConstructor(
            [new AsyncModifierModifierNode()],
            new ThisTierWithClauseWithClauseNode { Tier = new BorrowedSpecifierOwnershipSpecifierNode() });

        var refusals = f.Diagnostics.ToImmutableArray();
        Assert.Equal(["MIST0323", "MIST0341"], refusals.Select(diagnostic => diagnostic.Number.Value));
        Assert.All(refusals, diagnostic => Assert.Equal(MemberOffset + 1, diagnostic.Position().Start.Column));
    }

    [Fact]
    public void AConstructorThatRequiresAContextIsAdmitted()
    {
        var constructor = BuildConstructor(
            [],
            new ContextWithClauseWithClauseNode { Type = BindingFragments.TypeName("Int32"), Name = BindingFragments.Token("clock") });

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal("clock", Assert.Single(constructor.ContextBindingsList).Identifier);
    }

    private Diagnostic AssertRefusedOnce(string number)
    {
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(number, diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MemberOffset + 1, diagnostic.Position().Start.Column);
        return diagnostic;
    }

    private Method Build(IMethodHost host, MethodDeclarationNode declaration)
    {
        var resolver = f.Builder.Resolver;
        var parameters = new ParameterBuilder(resolver, f.Builder.Attributes, f.Diagnostics);
        new MethodBuilder(f.Builder, resolver, new DelegateResolver(f.Builder, parameters, resolver), new ConstraintResolver(f.Builder, resolver))
            .Resolve(declaration, host);
        return Assert.Single(host.MethodsList);
    }

    private Constructor BuildConstructor(ModifierNode[] modifiers, WithClauseNode clause)
    {
        var declaration = BindingFragments.At<ConstructorDeclarationNode>(MemberOffset);
        declaration.Modifiers = [.. modifiers];
        declaration.Name = BindingFragments.Token("Holder");
        declaration.WithClauses = [clause];
        declaration.Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode() };

        var host = Class(intrinsic: false);
        var resolver = f.Builder.Resolver;
        var parameters = new ParameterBuilder(resolver, f.Builder.Attributes, f.Diagnostics);
        var constraints = new ConstraintResolver(f.Builder, resolver);
        new ConstructorBuilder(f.Builder, new DelegateResolver(f.Builder, parameters, resolver), constraints).Resolve(declaration, host);
        return Assert.Single(host.ConstructorsList);
    }

    private ClassType Class(bool intrinsic)
    {
        return new ClassType("Holder", "Probe", f.Types, null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = intrinsic },
        };
    }

    private ExtensionBlock ExceptionExtension(MistAssemblyKind kind)
    {
        var assembly = new AssemblyScope(new ProgramScope(f.Built.Model), new MistAssembly("Owner", kind), f.Built.Model, new DiagnosticBag());
        var file = new FileScope(assembly, f.Built.Model, new DiagnosticBag());
        file.DeclareTopLevelType("Int32", f.Built.Type("Int32"));
        return new ExtensionBlock("self", "Standard", new ExtensionScope(file), null)
        {
            ResolvedReceiverType = new ClassType("Exception", "System", null, null, null),
        };
    }

    private static MethodDeclarationNode Method(bool intrinsic, bool withBody, string name = "Compute")
    {
        var method = BindingFragments.At<MethodDeclarationNode>(MemberOffset);
        method.Modifiers = intrinsic
            ? [new PublicModifierModifierNode(), new IntrinsicModifierModifierNode()]
            : [new PublicModifierModifierNode()];
        method.ReturnType = BindingFragments.TypeName("Int32");
        method.Name = BindingFragments.Token(name);
        method.Body = withBody
            ? new CallableBlockBodyCallableBodyNode { Block = new BlockNode() }
            : new CallableNoBodyCallableBodyNode();
        return method;
    }

    private static OwnershipSpecifierNode Tier(string tier)
    {
        return tier switch
        {
            "owned" => new OwnedSpecifierOwnershipSpecifierNode(),
            "borrowed" => new BorrowedSpecifierOwnershipSpecifierNode(),
            "frozen" => new FrozenSpecifierOwnershipSpecifierNode(),
            _ => new ReadonlyRefSpecifierOwnershipSpecifierNode(),
        };
    }
}