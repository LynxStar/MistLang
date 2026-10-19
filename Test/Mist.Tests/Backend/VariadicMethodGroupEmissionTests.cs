using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Mist.Tests.Semantics;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class VariadicMethodGroupEmissionTests
{
    [Theory]
    [InlineData(0, "() => Api.Describe_0(default(System.ValueTuple))")]
    [InlineData(1, "(__arg0) => Api.Describe_1<int>(System.ValueTuple.Create(__arg0))")]
    [InlineData(2, "(__arg0, __arg1) => Api.Describe_2<int, string>((__arg0, __arg1))")]
    public void FlatDelegateArgumentsArePackedForTheRecordedSpecialization(int width, string expected)
    {
        var built = new HandBuiltModel();
        var method = VariadicMethodGroupClosingTests.Describe(built, HandBuiltModel.Class("Api"));
        var inputs = new IMistType[] { built.Type("Int32"), built.Type("String") }.Take(width).ToArray();
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), null, method, new FunctionType(inputs, built.Type("Int32")))
        {
            TypeArguments = [built.Interner.LiftPack(method.TypeParametersList[0], new TypePackValue(inputs))],
        };
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(reference, writer, diagnostics);

        Assert.Contains(expected, writer.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}