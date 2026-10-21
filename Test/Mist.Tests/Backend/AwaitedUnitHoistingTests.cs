using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class AwaitedUnitHoistingTests
{
    private readonly HandBuiltModel model = new();

    private readonly ClassType owner = HandBuiltModel.Class("Program");

    private IMistType Void => model.Type("Void");

    [Fact]
    [Trait("Tier", "1")]
    public void AnAwaitedTaskOfVoid_IsHoistedWhereItsValueIsTaken()
    {
        var awaited = Awaited(Void);

        var plan = UnitHoisting.Plan(awaited);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(UnitHoisting.StepKind.Hoist, step.Kind);
        Assert.Same(awaited, step.Expression);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnAwaitedTaskOfAValue_IsLeftInPlace()
    {
        Assert.Empty(UnitHoisting.Plan(Awaited(model.Type("Int32"))).Steps);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ADiscardedAwaitedTaskOfVoid_IsNotHoisted()
    {
        Assert.Empty(UnitHoisting.Plan(Awaited(Void), discarded: true).Steps);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheAwaitRunsAheadOfTheDeclarationWithTheUnitInItsPlace()
    {
        var local = new LocalVariable("la", Void);
        var writer = new CSharpWriter();

        BodyEmitter.EmitStatement(new LocalDeclarationStatement(local, Awaited(Void)), writer, new DiagnosticBag());

        Assert.Contains("await LogAsync();\nVoid la = Standard.Void.Instance;", writer.ToString().ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private InvocationExpression Awaited(IMistType result)
    {
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
        var parameter = new TypeParameter("T", 0, task.Scope, task);
        task.TypeParametersList.Add(parameter);
        var drop = new Method("Drop", parameter, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task };
        var started = new ConstructedType(task, [result]);
        var logAsync = new Method("LogAsync", started, [], new MemberModifiers { IsStatic = true, IsAsync = true }) { DeclaringType = owner };
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), logAsync), [], started, logAsync, []);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), call, "Drop", result);
        return new InvocationExpression(HandBuiltModel.Node(), access, [], result, drop, []);
    }
}