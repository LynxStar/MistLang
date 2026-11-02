using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Construction;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ClrCatchEmissionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvocationAndCompletionShareOneCatchWithPreparedArgumentsOutside(bool task, bool unit)
    {
        var shapes = new ResultModel();
        var success = unit ? shapes.Void : shapes.Integer;
        var returned = task ? shapes.Built.Interner.GetOrCreateConstructedType(shapes.Built.Type("Task<>"), [success]) : success;
        var function = new FunctionType([shapes.Text], returned);
        var result = shapes.Result(success, shapes.Text);
        var type = task ? shapes.Built.Interner.GetOrCreateConstructedType(shapes.Built.Type("Task<>"), [result]) : result;
        var call = new InvocationExpression(Node(), new MethodReferenceExpression(Node(), ClrCatchBindingTests.Catch(shapes)),
            [Call("PrepareCallable", function), Call("PrepareArgument", shapes.Text)], type, ClrCatchBindingTests.Catch(shapes), [function, shapes.Text]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitExpression(call, writer, diagnostics);
        var text = writer.ToString();

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(1, text.Split("catch (System.Exception ").Length - 1);
        Assert.Equal(1, text.Split("PrepareCallable(").Length - 1);
        Assert.Equal(1, text.Split("PrepareArgument(").Length - 1);
        Assert.True(text.IndexOf("catch (", StringComparison.Ordinal) < text.IndexOf("PrepareCallable(", StringComparison.Ordinal));
        Assert.True(text.IndexOf("PrepareCallable(", StringComparison.Ordinal) < text.IndexOf("PrepareArgument(", StringComparison.Ordinal));
        Assert.Contains(".Failure(__", text);
        Assert.DoesNotContain("new System.Exception", text);
        Assert.Equal(task, text.Contains("await ", StringComparison.Ordinal));
        Assert.Equal(unit, text.Contains(".Success(Standard.Void.Instance)", StringComparison.Ordinal));
        if(task)
        {
            Assert.True(text.IndexOf("try", StringComparison.Ordinal) < text.IndexOf("await ", StringComparison.Ordinal));
            Assert.True(text.IndexOf("await ", StringComparison.Ordinal) < text.IndexOf("catch (", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ExpandedSpreadPreparesItsEffectfulTupleOnce()
    {
        var shapes = new ResultModel();
        var tuple = new TupleType([shapes.Text, shapes.Text]);
        var source = Call("ReadPack", tuple);
        var function = new FunctionType([shapes.Text, shapes.Text], shapes.Integer);
        var method = ClrCatchBindingTests.Catch(shapes);
        var call = new InvocationExpression(Node(), new MethodReferenceExpression(Node(), method),
            [Call("PrepareCallable", function), new MemberAccessExpression(Node(), source, "Item1", shapes.Text),
                new MemberAccessExpression(Node(), source, "Item2", shapes.Text)],
            shapes.Result(shapes.Integer, shapes.Text), method, null);
        var writer = new CSharpWriter();
        BodyEmitter.EmitExpression(call, writer, new DiagnosticBag());
        Assert.Equal(1, writer.ToString().Split("ReadPack(").Length - 1);
        Assert.Contains(".Item2.Item1,", writer.ToString());
        Assert.Contains(".Item2.Item2)", writer.ToString());
    }

    [Fact]
    public void RawTaskInvocationHasNoInterception()
    {
        var shapes = new ResultModel();
        var task = shapes.Built.Interner.GetOrCreateConstructedType(shapes.Built.Type("Task<>"), [shapes.Integer]);
        var writer = new CSharpWriter();
        BodyEmitter.EmitExpression(Call("RawTask", task), writer, new DiagnosticBag());
        Assert.Equal("RawTask()", writer.ToString());
    }

    private static InvocationExpression Call(string name, IMistType type)
    {
        return new InvocationExpression(Node(), new MethodReferenceExpression(Node(), new Method(name, type, [], new MemberModifiers { IsStatic = true })), [], type, null, null);
    }
}