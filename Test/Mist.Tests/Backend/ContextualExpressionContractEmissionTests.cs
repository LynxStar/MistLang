using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ContextualExpressionContractEmissionTests
{
    [Theory]
    [InlineData("local", "stored = Next()")]
    [InlineData("field", "holder.Field = Next()")]
    [InlineData("property", "holder.Property = Next()")]
    [InlineData("index", "holder[0] = Next()")]
    public void AStorageTypedAssignmentEmitsOneEvaluationOfItsNarrowerValue(string shape, string expected)
    {
        var built = new HandBuiltModel();
        var integer = built.Type("Int32");
        var storage = built.Type("Int64");
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("holder", HandBuiltModel.Class("Holder")));
        Expression target = shape switch
        {
            "field" => new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Field", storage),
            "property" => new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Property", storage),
            "index" => new IndexExpression(HandBuiltModel.Node(), receiver, HandBuiltModel.Literal(integer, 0L), storage),
            _ => new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("stored", storage)),
        };
        var value = Next(integer);
        var assignment = new AssignmentExpression(HandBuiltModel.Node(), target, value, storage);

        var emitted = Emit(new LocalDeclarationStatement(new LocalVariable("result", assignment.Type), assignment));

        Assert.StartsWith("long result = ", emitted, StringComparison.Ordinal);
        Assert.Contains(expected, emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("Next()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ADeclaredConversionKeepsOneValueEvaluationInsideTheAssignment()
    {
        var source = HandBuiltModel.Class("Source");
        var destination = HandBuiltModel.Class("Destination");
        var conversion = HandBuiltModel.Conversion(destination, source, destination);
        var converted = new ImplicitConversionExpression(HandBuiltModel.Node(), Next(source), destination,
            [new ConversionFunction(conversion, source, destination)]);
        var target = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("stored", destination));
        var assignment = new AssignmentExpression(HandBuiltModel.Node(), target, converted, destination);

        var emitted = Emit(new LocalDeclarationStatement(new LocalVariable("result", destination), assignment));

        Assert.StartsWith("Destination result = stored = ", emitted, StringComparison.Ordinal);
        Assert.Contains("((Destination)(Next()))", emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("Next()", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStringSwitchReceiverEmitsItsMemberAtAnIntegerLocalOrReturn(bool returns)
    {
        var built = new HandBuiltModel();
        var integer = built.Type("Int32");
        var text = built.Type("String");
        var scrutinee = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("source", integer));
        var selection = new SwitchExpression(HandBuiltModel.Node(), scrutinee,
            [new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), integer, null), null, HandBuiltModel.Literal(text, "five"))], text);
        var length = new MemberAccessExpression(HandBuiltModel.Node(), selection, "Length", integer);

        var emitted = Emit(returns
            ? new ReturnStatement(length, integer)
            : new LocalDeclarationStatement(new LocalVariable("length", integer), length));

        Assert.Contains("(source switch { var _ => \"five\" }).Length", emitted, StringComparison.Ordinal);
        Assert.StartsWith(returns ? "return " : "int length = ", emitted, StringComparison.Ordinal);
    }

    private static InvocationExpression Next(IMistType type)
    {
        var method = new Method("Next", type, [], DeclarationFragments.Public with { IsStatic = true }) { DeclaringType = HandBuiltModel.Class("Effects") };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], type, method, []);
    }

    private static string Emit(Statement statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitStatement(statement, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Trim();
    }
}