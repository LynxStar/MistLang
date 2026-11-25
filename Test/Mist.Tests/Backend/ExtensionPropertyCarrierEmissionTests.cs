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
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ExtensionPropertyCarrierEmissionTests
{
    private static readonly ClassType Counter = new("Counter", "Probe", null, null, null);

    [Theory]
    [InlineData(false, false, "this Counter self", "return self.Value;")]
    [InlineData(false, true, "this Counter self", "int read = self.Value;\nreturn read;")]
    [InlineData(true, false, "", "return 7;")]
    [InlineData(true, true, "", "int read = 7;\nreturn read;")]
    public void APropertyBecomesAGetterTakingTheReceiverOnlyAsAnInstanceMember(
        bool isStatic, bool blockBodied, string parameters, string body)
    {
        Expression value = isStatic ? HandBuiltModel.Literal(IntegerType, 7) : SelfMember("Value", IntegerType);
        var read = new LocalVariable("read", IntegerType);
        var getter = blockBodied
            ? new PropertyAccessor
            {
                Body = HandBuiltModel.Block(
                    new LocalDeclarationStatement(read, value),
                    new ReturnStatement(new LocalReferenceExpression(HandBuiltModel.Node(), read), IntegerType)),
            }
            : new PropertyAccessor { ExpressionBody = value };
        var block = Block(new Property("Doubled", IntegerType, new MemberModifiers { IsStatic = isStatic }) { Getter = getter });

        var indented = string.Join("\n", body.Split('\n').Select(line => "        " + line));
        Assert.Equal(
            "public static class CounterExtensions\n{\n"
            + $"    public static int get_Doubled({parameters})\n    {{\n{indented}\n    }}\n}}",
            Emit(block));
    }

    [Fact]
    public void AnExpressionGetterEntersItsValueIntoThePropertysType()
    {
        var three = ArrayOf(IntegerType, 3);
        var sized = new UnionType([three, ArrayOf(IntegerType, 5)]);
        var block = Block(new Property("Picked", sized, default)
        {
            Getter = new PropertyAccessor { ExpressionBody = SelfMember("Three", three) },
        });

        Assert.Contains(
            "return Union_Int32Array3_Int32Array5.FromInt32Array3(self.Three);",
            Emit(block).Split('\n').Select(line => line.Trim()));
    }

    [Fact]
    public void APropertyWithoutAGetterLeavesNoMemberAndNoGapInTheCarrier()
    {
        var block = Block(
            new Property("Sink", IntegerType, default)
            {
                Setter = new PropertyAccessor { Body = HandBuiltModel.Block() },
            },
            new Property("Seed", IntegerType, new MemberModifiers { IsStatic = true })
            {
                Getter = new PropertyAccessor { ExpressionBody = HandBuiltModel.Literal(IntegerType, 7) },
            });
        block.MethodsList.Add(new Method("Peek", IntegerType, [], default)
        {
            Body = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(IntegerType, 1), IntegerType)),
        });

        Assert.Equal(
            "public static class CounterExtensions\n{\n"
            + "    public static int Peek(this Counter self)\n    {\n        return 1;\n    }\n\n"
            + "    public static int get_Seed()\n    {\n        return 7;\n    }\n}",
            Emit(block));
    }

    private static MemberAccessExpression SelfMember(string name, IMistType type)
    {
        var self = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("self", Counter));
        return new MemberAccessExpression(HandBuiltModel.Node(), self, name, type);
    }

    private static ExtensionBlock Block(params Property[] properties)
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null)
        {
            CarrierName = "CounterExtensions",
            ResolvedReceiverType = Counter,
        };
        block.PropertiesList.AddRange(properties);
        return block;
    }

    private static string Emit(ExtensionBlock block)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        BodyEmitter.ResetTemporaries();
        ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted);

        Assert.True(anyEmitted);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().ReplaceLineEndings("\n").Trim();
    }
}