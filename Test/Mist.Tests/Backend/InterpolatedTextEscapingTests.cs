using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class InterpolatedTextEscapingTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("} done", "$\"set {n}}} done\"")]
    [InlineData("{ open", "$\"set {n}{{ open\"")]
    [InlineData(" plain", "$\"set {n} plain\"")]
    public void BracesInLiteralText_AreDoubledForCSharp(string tail, string expected)
    {
        var text = model.Type("String");
        var hole = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("n", text));
        var interpolated = new InterpolatedStringExpression(
            HandBuiltModel.Node(), [new InterpolatedStringText("set "), new InterpolatedStringHole(hole), new InterpolatedStringText(tail)], text);

        Assert.Equal(expected, InterpolationEmitter.Emit(interpolated, _ => "n"));
    }
}