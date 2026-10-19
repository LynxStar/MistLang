using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class FailedParameterOverloadTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedSignatureDoesNotAddACallSiteRefusal(bool failed)
    {
        var built = new HandBuiltModel();
        var method = new Method("M", built.Type("Int32"),
            [new Parameter("args", ErrorType.Instance) { IsFailed = failed }], default);
        var diagnostics = new DiagnosticBag();

        OverloadFailure.Report(new OverloadChoice<Method> { Selected = null, NoArityMatch = true }, [method],
            new Expression[] { HandBuiltModel.Literal(built.Type("Int32")), HandBuiltModel.Literal(built.Type("String")) },
            "method 'M'", HandBuiltModel.Node(), diagnostics);

        Assert.Equal(failed ? 0 : 1, diagnostics.ToImmutableArray().Length);
    }
}