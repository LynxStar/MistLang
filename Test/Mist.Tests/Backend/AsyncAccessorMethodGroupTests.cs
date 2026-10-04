using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AsyncAccessorMethodGroupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PrivateMethodGroup_AccessorCompletesOnlyPromotedTargets(bool instance, bool promoted)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        _ = Task(model);
        var source = HandBuiltModel.Class("Reader");
        var clone = HandBuiltModel.Class("Copy");
        var conversion = HandBuiltModel.Conversion(clone, source, clone);
        var method = new Method("Read", integer, [],
            new MemberModifiers { Accessibility = Accessibility.Private, IsStatic = !instance })
        { DeclaringType = source };
        var receiver = instance ? new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("reader", source)) : null;
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method, new FunctionType([], integer));
        if(promoted)
        {
            model.Model.TaskDrops.RecordDrop(method);
        }

        using var promotion = AsyncPromotion.Enter(model.Model);
        var diagnostics = new DiagnosticBag();
        using var reach = UnsafeReach.Enter(clone, conversion, "__Out", diagnostics);
        var actual = UnsafeReach.Function(reference, _ => "Next()")!;

        Assert.NotNull(actual);
        Assert.Equal(promoted, actual.Contains(".GetAwaiter().GetResult()", StringComparison.Ordinal));
        Assert.Contains("System.Func<int>", actual, StringComparison.Ordinal);
        Assert.Contains(promoted ? "System.Threading.Tasks.Task<int>" : "extern int", string.Join("\n", reach.Declarations.SelectMany(lines => lines)), StringComparison.Ordinal);
        if(instance)
        {
            Assert.StartsWith("____Out_Bound(Next(), __t =>", actual, StringComparison.Ordinal);
            Assert.Equal(1, actual.Split("Next()", StringSplitOptions.None).Length - 1);
            Assert.Contains("____Out_Probe_Reader_Read(__t)", actual, StringComparison.Ordinal);
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void PrivateDeclaredAsyncMethodGroup_KeepsItsTaskDelegateResult()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var result = model.Interner.GetOrCreateConstructedType(Task(model), [integer]);
        var source = HandBuiltModel.Class("Reader");
        var clone = HandBuiltModel.Class("Copy");
        var conversion = HandBuiltModel.Conversion(clone, source, clone);
        var method = new Method("Read", result, [],
            new MemberModifiers { Accessibility = Accessibility.Private, IsStatic = true, IsAsync = true })
        { DeclaringType = source };
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), null, method, new FunctionType([], result));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        var diagnostics = new DiagnosticBag();
        using var reach = UnsafeReach.Enter(clone, conversion, "__Out", diagnostics);
        var actual = UnsafeReach.Function(reference, _ => throw new InvalidOperationException())!;

        Assert.Contains("System.Func<System.Threading.Tasks.Task<int>>", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter", actual, StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateClosedGenericMethodGroup_PreservesItsClosedCallArguments(bool promoted)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        _ = Task(model);
        var source = HandBuiltModel.Class("Reader");
        var clone = HandBuiltModel.Class("Copy");
        var conversion = HandBuiltModel.Conversion(clone, source, clone);
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var method = new Method("Read", parameter, [], new MemberModifiers { Accessibility = Accessibility.Private, IsStatic = true })
        { DeclaringType = source };
        method.TypeParametersList.Add(parameter);
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), null, method, new FunctionType([], integer))
        { TypeArguments = [integer] };
        if(promoted)
        {
            model.Model.TaskDrops.RecordDrop(method);
        }

        using var promotion = AsyncPromotion.Enter(model.Model);
        var diagnostics = new DiagnosticBag();
        using var reach = UnsafeReach.Enter(clone, conversion, "__Out", diagnostics);
        var actual = UnsafeReach.Function(reference, _ => throw new InvalidOperationException())!;

        Assert.Contains("____Out_Probe_Reader_Read<int>(null)", actual, StringComparison.Ordinal);
        Assert.Equal(promoted, actual.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static IMistType Task(HandBuiltModel model)
    {
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
        task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, task));
        _ = model.Model.RegisterType(task);
        return task;
    }
}