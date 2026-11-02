using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ClrCatchSelectionTests
{
    [Fact]
    public void ApplicabilityAcceptsTheAdaptedTargetAndRefusesTheUnadaptedOne()
    {
        var fixture = new Fixture();
        var adapted = new FunctionType([fixture.Operation], fixture.Adapted);
        var declared = new FunctionType([fixture.Operation], fixture.Declared);

        Assert.True(MethodGroupConversion.Accepts(fixture.Group, adapted, fixture.Interner, fixture.ConstructTask));
        Assert.False(MethodGroupConversion.Accepts(fixture.Group, declared, fixture.Interner, fixture.ConstructTask));

        // Without the task-construction seam the group is held to its declared Result<Task<T>, E>, the gap
        // that let only task creation through; the adapted boundary is then the one position it misses.
        Assert.False(MethodGroupConversion.Accepts(fixture.Group, adapted, fixture.Interner));
        Assert.True(MethodGroupConversion.Accepts(fixture.Group, declared, fixture.Interner));
    }

    [Fact]
    public void ResultInferenceReadsTheAdaptedTaskReturn()
    {
        var fixture = new Fixture();

        var adapted = MethodGroupConversion.ResultType(fixture.Group, [fixture.Operation], fixture.Interner, fixture.ConstructTask);
        var intercepted = ResultTypes.Of(TaskResult.Of(adapted!));
        Assert.NotNull(intercepted);
        Assert.Same(fixture.Built.Type("Int32"), intercepted.Success);

        var declared = MethodGroupConversion.ResultType(fixture.Group, [fixture.Operation], fixture.Interner);
        Assert.Null(TaskResult.Of(declared!));
        Assert.Same(fixture.ForeignReturn, ResultTypes.Of(declared!)!.Success);
    }

    [Fact]
    public void OverloadSelectionChoosesTheAdaptedBoundaryOverTheTaskCreationShape()
    {
        var fixture = new Fixture();
        var toAdapted = fixture.Outer("ToAdapted", new FunctionType([fixture.Operation], fixture.Adapted));
        var toDeclared = fixture.Outer("ToDeclared", new FunctionType([fixture.Operation], fixture.Declared));

        var chosen = OverloadResolution.Select(
            [toAdapted, toDeclared], [fixture.Group], null, null, fixture.Interner, constructTask: fixture.ConstructTask);
        Assert.Same(toAdapted, chosen.Selected);

        var unseamed = OverloadResolution.Select([toAdapted, toDeclared], [fixture.Group], null, null, fixture.Interner);
        Assert.Same(toDeclared, unseamed.Selected);
    }

    [Fact]
    public void TypeInferenceLearnsTheAdaptedReturnFromACatchGroup()
    {
        var fixture = new Fixture();
        var r = new TypeParameter("R", 0, new Scope(null), null);
        var infer = new Method("Infer", r, [new Parameter("f", new FunctionType([fixture.Operation], r))],
            new MemberModifiers { IsStatic = true });
        infer.TypeParametersList.Add(r);

        var learned = OverloadResolution.Select([infer], [fixture.Group], null, null, fixture.Interner, constructTask: fixture.ConstructTask);
        var intercepted = ResultTypes.Of(TaskResult.Of(learned.Substitution.Apply(infer.ReturnType, fixture.Interner)));
        Assert.NotNull(intercepted);
        Assert.Same(fixture.Built.Type("Int32"), intercepted.Success);

        var unseamed = OverloadResolution.Select([infer], [fixture.Group], null, null, fixture.Interner);
        var declared = unseamed.Substitution.Apply(infer.ReturnType, fixture.Interner);
        Assert.Null(TaskResult.Of(declared));
        Assert.Same(fixture.ForeignReturn, ResultTypes.Of(declared)!.Success);
    }

    private sealed class Fixture
    {
        private readonly BindingFragments f = new();
        private readonly ResultModel shapes = new();

        public HandBuiltModel Built => f.Built;
        public TypeInterner Interner => f.Built.Interner;
        public Func<IMistType, IMistType> ConstructTask => f.Builder.Resolver.ConstructTask;
        public ConstructedType ForeignReturn { get; }
        public FunctionType Operation { get; }
        public ConstructedType Adapted { get; }
        public ConstructedType Declared { get; }
        public MethodGroupExpression Group { get; }

        public Fixture()
        {
            var task = f.Built.Type("Task<>");
            var parameter = new TypeParameter("T", 0, task.Scope, task);
            task.TypeParametersList.Add(parameter);
            task.MethodsList.Add(new Method("Drop", parameter, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task });
            f.Register(task);

            ForeignReturn = f.Built.Interner.GetOrCreateConstructedType(task, [f.Built.Type("Int32")]);
            Operation = new FunctionType([], ForeignReturn);
            Adapted = f.Built.Interner.GetOrCreateConstructedType(task, [shapes.Result(f.Built.Type("Int32"), shapes.Text)]);
            Declared = shapes.Result(ForeignReturn, shapes.Text);
            var method = ClrCatchBindingTests.Catch(shapes);
            Group = new MethodGroupExpression(Node(), null, "Catch", [method], null);
        }

        public Method Outer(string name, FunctionType takes)
        {
            return new Method(name, f.Built.Type("Int32"), [new Parameter("f", takes)], new MemberModifiers { IsStatic = true });
        }
    }
}