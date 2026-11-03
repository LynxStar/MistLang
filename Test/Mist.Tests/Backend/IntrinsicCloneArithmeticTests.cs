using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class IntrinsicCloneArithmeticTests
{
    [Theory]
    [InlineData("Slash", "DivideChecked")]
    [InlineData("Modulo", "RemainderChecked")]
    public void FallibleNumericClonesWrapSuccessAndRetainTheErrorAfterOneCheckedCall(string operation, string helper)
    {
        var kind = Enum.Parse<OperatorKind>(operation);
        var model = new ResultModel();
        var source = (ClassType)model.Integer;
        var error = model.Built.Type("DivisionError");
        var clone = new StructType("Count", "Probe", null, null, null) { CloneSource = source };
        var original = new Operator(kind, model.Result(source, error), [], default);
        var renamed = new Operator(kind, model.Result(clone, error),
            [new Parameter("a", clone), new Parameter("b", clone)], default);
        var writer = new CSharpWriter();
        Assert.True(IntrinsicCloneArithmetic.Emit(renamed, original, clone, ["a.Value", "b.Value"], writer, new DiagnosticBag()));
        var emitted = writer.ToString();
        Assert.Equal(1, emitted.Split($"Standard.ResultArithmetic.{helper}(").Length - 1);
        Assert.Contains(".Failure(inspected.", emitted);
        Assert.Matches(@"\.Success\(new (?:Probe\.)?Count\(inspected\.", emitted);
        Assert.DoesNotContain("a.Value / b.Value", emitted);
    }
}