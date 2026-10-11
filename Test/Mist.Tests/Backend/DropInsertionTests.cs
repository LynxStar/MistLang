// Which bindings and fields a release reaches, decided one function at a time over types built by
// hand. How a linear local's release follows the executed path is LocalLifetimeTests' and how it is
// written is LocalLifetimeEmissionTests'; these are the decisions both stand on.

using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class DropInsertionTests
{
    private readonly LifetimeFragments f = new();

    /// <summary>
    /// Affine allows forgetting and unrestricted owes nothing, so a release there would be a call the
    /// author declined to make; linear and relevant both owe one.
    /// </summary>
    [Fact]
    public void OnlyLinearAndRelevantBindingsAreReleasedAtScopeExit()
    {
        Assert.Equal(
            [LifetimeRelease.AtExit, LifetimeRelease.AtExit, LifetimeRelease.None, LifetimeRelease.None],
            new[] { f.Token, f.Shared, f.Once, f.Plain }.Select(type => Unplanned(f.Local("x", type))));
    }

    /// <summary>
    /// A linear local the plan covers is released as the plan says, whatever its remainder holds.
    /// </summary>
    [Fact]
    public void ACoveredLinearLocalFollowsItsPlan()
    {
        var t = f.Local("t");
        var declaration = Declare(t, f.New(1));
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement> { [t] = declaration });

        Assert.Equal(LifetimeRelease.None, DropInsertion.ReleaseOf(declaration, [], plan));

        plan.Owe(t);
        Assert.Equal(LifetimeRelease.AtExit, DropInsertion.ReleaseOf(declaration, [], plan));

        plan.Mark(Ref(t), t, owned: false);
        Assert.Equal(LifetimeRelease.Guarded, DropInsertion.ReleaseOf(declaration, [], plan));
    }

    /// <summary>
    /// A relevant value is aliasable, so a local transfer leaves both holders releasing it; anything it
    /// is handed to elsewhere still elides the release, which is the approximation a task's promotion reads.
    /// </summary>
    [Fact]
    public void ARelevantLocalKeepsTheSubtreeApproximation()
    {
        var s = f.Local("s", f.Shared);
        var alias = f.Local("alias", f.Shared);
        var take = new Method("Keep", f.Void, [new Parameter("s", f.Shared)], new MemberModifiers { IsStatic = true });

        Assert.Equal(LifetimeRelease.AtExit, DropInsertion.ReleaseOf(Declare(s, f.New(1, f.Shared)), [Declare(alias, Ref(s))], LifetimePlan.Empty));
        Assert.Equal(LifetimeRelease.None, DropInsertion.ReleaseOf(Declare(s, f.New(1, f.Shared)), [Run(f.Call(take, Ref(s)))], LifetimePlan.Empty));
    }

    [Fact]
    public void APlanReleasesNothingItDoesNotCover()
    {
        var t = f.Local("t");

        Assert.False(LifetimePlan.Empty.Covers(Declare(t, f.New(1))));
        Assert.Equal(LifetimeRelease.None, LifetimePlan.Empty.ReleaseOf(Declare(t, f.New(1))));
    }

    /// <summary>
    /// A mark is lowered only for a guarded local: an unconditional or absent release has no flag for
    /// it to change.
    /// </summary>
    [Fact]
    public void MarksAreReadOnlyForAGuardedLocal()
    {
        var t = f.Local("t");
        var u = f.Local("u");
        var at = Ref(t);
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement>
        {
            [t] = Declare(t, f.New(1)),
            [u] = Declare(u, f.New(2)),
        });

        plan.Owe(t);
        plan.Mark(at, t, owned: false);
        plan.Mark(at, u, owned: false);

        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(at));
    }

    [Fact]
    public void AnUninitializedLocalIsAcquiredWhereItIsFirstAssigned()
    {
        var r = f.Local("r");
        var declaration = Declare(r, null);
        var assignment = Assign(r, f.New(1));
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement> { [r] = declaration });

        Assert.Null(plan.AcquiredAt(declaration));

        plan.Mark(assignment, r, owned: true);
        Assert.Same(assignment.Node, plan.AcquiredAt(declaration));
    }

    /// <summary>
    /// A drop that hands back what it held passes the obligation on with the value.
    /// </summary>
    [Fact]
    public void ADropReturningADroppableValueChains()
    {
        Assert.Equal("(b.Drop()).Drop()", DropInsertion.DropChain("b", Box()));
    }

    /// <summary>
    /// A container under an obligation cannot honour it without releasing what it holds, and writing no
    /// Drop is not declining -- there is nothing to write that the fields do not already say.
    /// </summary>
    [Fact]
    public void AContainerWithDroppableFieldsGetsASynthesizedDropInDeclarationOrder()
    {
        var pair = Container("Pair", "First", "Second");

        Assert.True(DropInsertion.SynthesizesDrop(pair));
        Assert.Equal(
            ["this.First.Drop();", "this.Second.Drop();"],
            DropInsertion.DroppableFields(pair).Select(DropInsertion.FieldDrop));
    }

    /// <summary>
    /// Writing a Drop does not discharge the obligation: what the author wrote happens IN ADDITION to
    /// releasing what the type holds, and a field the declared drop hands back is moved instead.
    /// </summary>
    [Fact]
    public void ADeclaredDropOwesEveryFieldItDoesNotHandBack()
    {
        var logged = Container("Logged", "Inner");
        var box = Box();
        var silent = new Method("Drop", f.Void, [], default) { Body = HandBuiltModel.Block() };
        var handing = box.MethodsList.Single(method => method.Name == "Drop");

        Assert.False(DropInsertion.SynthesizesDrop(box));
        Assert.Equal(["Inner"], DropInsertion.FieldsOwedBy(silent, logged).Select(field => field.Name));
        Assert.Empty(DropInsertion.FieldsOwedBy(handing, box));
    }

    /// <summary>
    /// A linear parameter is released as its plan says; a relevant one keeps the whole-body reading, with
    /// a hand-off recognised by the binding it reaches and not by a local that shares its name.
    /// </summary>
    [Fact]
    public void AParameterIsReleasedAsItsPlanSaysOrElseAsItsBodyReads()
    {
        var keep = new Method("Keep", f.Void, [new Parameter("s", f.Shared)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var linear = new Parameter("t", f.Token);
        var kept = new Parameter("s", f.Shared);
        var passed = new Parameter("p", f.Shared);
        var namesake = f.Local("s", f.Shared);
        var method = f.Function("Use", HandBuiltModel.Block(Run(f.Call(keep, Ref(passed))), Run(f.Call(keep, Ref(namesake)))), linear, kept, passed);
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement>());

        plan.Cover(linear);
        plan.Owe(linear);
        plan.Mark(Ref(linear), linear, owned: false);

        Assert.Equal(
            [(linear, LifetimeRelease.Guarded), (kept, LifetimeRelease.AtExit)],
            DropInsertion.ReleasedParameters(method, method.Body!, plan));
    }

    [Fact]
    public void AFieldIsReleasedAsItsPlanSaysOrElseAsItsDropReads()
    {
        var shared = new Field("Log", f.Shared, OwnershipKind.Unrestricted, default) { DeclaringType = f.Holder };
        f.Holder.FieldsList.Add(shared);
        var drop = f.HolderDrop(HandBuiltModel.Block(Run(DropOf(Ref(f.First)))));
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement>());

        plan.Cover(f.First);
        plan.Cover(f.Second);
        plan.Owe(f.Second);

        Assert.Equal(
            [(f.Second, LifetimeRelease.AtExit), (shared, LifetimeRelease.AtExit)],
            DropInsertion.ReleasedFields(drop, f.Holder, plan));
    }

    private static LifetimeRelease Unplanned(LocalVariable local)
    {
        var definition = (ClassType)local.Type;
        var made = new NewExpression(HandBuiltModel.Node(), definition, [], definition.ConstructorsList[0], [], []);

        return DropInsertion.ReleaseOf(Declare(local, made), [], LifetimePlan.Empty);
    }

    private ClassType Container(string name, params string[] fields)
    {
        var container = HandBuiltModel.Class(name);
        container.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };

        foreach(var field in fields)
        {
            container.FieldsList.Add(new Field(field, f.Token, default, default) { DeclaringType = container });
        }

        return container;
    }

    private ClassType Box()
    {
        var box = Container("Box", "Inner");
        var inner = box.FieldsList[0];
        box.MethodsList.Add(new Method("Drop", f.Token, [], default)
        {
            DeclaringType = box,
            Body = HandBuiltModel.Block(Return(new FieldReferenceExpression(HandBuiltModel.Node(), inner))),
        });

        return box;
    }
}