using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class RelevantWithCleanupTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void ExplicitAndWithDropsRetainTheRelevantLocalsLexicalRelease()
    {
        var r = f.Local("r", f.Shared);
        var scope = With(Ref(r), HandBuiltModel.Block(Run(Drop(r)), Run(f.Call(f.Risky))));
        var declaration = Declare(r, f.New(1, f.Shared));
        var emitted = Emitted(HandBuiltModel.Block(declaration, scope));

        Assert.Equal(LifetimeRelease.AtExit, DropInsertion.ReleaseOf(declaration, [scope], LifetimePlan.Empty));
        Assert.Equal(3, emitted.Split(".Drop()", StringSplitOptions.None).Length - 1);
        Assert.Contains("MistContext.Pop<Shared>();", emitted, StringComparison.Ordinal);
        Assert.EndsWith("r.Drop();\n}", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitRelevantTaskDropAwaitsTheResultAndRetainsTheLexicalAwait()
    {
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null)
        { Modifiers = new MemberModifiers { IsIntrinsic = true, Discipline = SubstructuralDiscipline.Relevant } };
        task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, task));
        var drop = new Method("Drop", f.Int32, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task };
        task.MethodsList.Add(drop);
        var taskInt = new ConstructedType(task, [f.Int32]);
        var held = f.Local("task", taskInt);
        var result = f.Local("v", f.Int32);
        var start = new Method("Start", taskInt, [], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var body = HandBuiltModel.Block(Declare(held, f.Call(start)), Declare(result, Drop(held)));
        var emitted = EmittedMethod(f.Function("Run", body));

        Assert.Contains("int v = await task;", emitted, StringComparison.Ordinal);
        Assert.Contains("finally", emitted, StringComparison.Ordinal);
        Assert.EndsWith("await task;\n}", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void InstallingAnExplicitLoanDoesNotAcquireCleanup(OwnershipKind ownership, bool byRef)
    {
        var loan = new LocalVariable("loan", f.Token) { Ownership = ownership, IsRef = byRef, IsReadonly = byRef };
        var binding = f.Local("binding");
        var emitted = Emitted(HandBuiltModel.Block(With(Ref(loan), HandBuiltModel.Block(), binding)));

        Assert.DoesNotContain(".Drop()", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ReinstallingAMethodContextBindingDoesNotAcquireCleanup()
    {
        var context = new LocalVariable("context", f.Token) { IsContextBound = true };
        var binding = f.Local("binding");
        var emitted = Emitted(HandBuiltModel.Block(With(Ref(context), HandBuiltModel.Block(), binding)));

        Assert.DoesNotContain(".Drop()", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ReinitializingThroughARefRestoresTheHoldersIndependentCleanup()
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { IsRef = true };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), Run(Assign(lease, f.New(2))))));
        var emitted = Emitted(body);

        Assert.Equal(2, emitted.Split(".Drop()", StringSplitOptions.None).Length - 1);
        Assert.Contains("MistOwnership.Taken(lease =", emitted, StringComparison.Ordinal);
        Assert.Contains("r.Drop();", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void ANestedExplicitLoanLeavesOnlyTheOuterScopesCleanup(OwnershipKind ownership, bool byRef)
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { Ownership = ownership, IsRef = byRef, IsReadonly = byRef };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), With(Ref(lease), HandBuiltModel.Block()))));

        Assert.Equal(1, Emitted(body).Split(".Drop()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void FreshValuesOnEveryBranchRetainTheirOwnCleanup()
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            f.If(HandBuiltModel.Block(Run(Assign(r, f.New(2)))), HandBuiltModel.Block(Run(Assign(r, f.New(3))))))));
        var emitted = Emitted(body);

        Assert.Equal(2, emitted.Split(".Drop()", StringSplitOptions.None).Length - 1);
        Assert.Contains("MistOwnership.Taken(r =", emitted, StringComparison.Ordinal);
    }
}