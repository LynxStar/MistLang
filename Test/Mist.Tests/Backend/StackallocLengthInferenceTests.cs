using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class StackallocLengthInferenceTests : ProgramModelProbe
{
    public StackallocLengthInferenceTests()
        : base("MistStackallocLength_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("var", "Standard.Span", "0", 0)]
    [InlineData("var", "Standard.Span", "2", 2)]
    [InlineData("var", "Standard.Span", "0x2", 2)]
    [InlineData("var", "Standard.Span", "2147483647", int.MaxValue)]
    [InlineData("Standard.Span<int, unknown>", "Standard.Span", "2", 2)]
    [InlineData("Standard.ReadOnlySpan<int, unknown>", "Standard.ReadOnlySpan", "0", 0)]
    [InlineData("Standard.ReadOnlySpan<int, unknown>", "Standard.ReadOnlySpan", "2", 2)]
    public void IntegerLiteralLengthsShareTheExplicitDependentTypesInternedIdentity(
        string target, string family, string size, int length)
    {
        var model = Build(Source($$"""
            {{family}}<int, {{length}}> expected = default({{family}}<int, {{length}}>);
            {{target}} actual = stackalloc int[{{size}}];
            """), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var declarations = Declarations(model);
        var allocation = Assert.IsType<StackallocExpression>(declarations[1].Initializer);
        var result = Assert.IsType<ConstructedType>(allocation.Type);
        var lifted = Assert.IsType<LiftedType>(result.TypeArguments[1]);

        Assert.Same(declarations[0].Local.Type, result);
        Assert.Same(model.TypesByFQN["Standard.Int32"], lifted.Type);
        Assert.Equal((long)length, lifted.Value);
        Assert.Equal((long)length, Assert.IsType<LiteralExpression>(allocation.Size).Value);
    }

    [Theory]
    [InlineData("var", "Standard.Span<,>")]
    [InlineData("Standard.ReadOnlySpan<int, unknown>", "Standard.ReadOnlySpan<,>")]
    public void ComputedSizesRetainTheirInvocationAndUnknownIntegerLength(string target, string family)
    {
        var model = Build(Source($"{target} actual = stackalloc int[Size()];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var allocation = Assert.IsType<StackallocExpression>(Assert.Single(Declarations(model)).Initializer);
        var result = Assert.IsType<ConstructedType>(allocation.Type);
        var length = Assert.IsType<UnknownValueType>(result.TypeArguments[1]);
        var invocation = Assert.IsType<InvocationExpression>(allocation.Size);

        Assert.Equal(family, result.Definition.FullyQualifiedName);
        Assert.Same(model.TypesByFQN["Standard.Int32"], length.Domain);
        Assert.NotNull(invocation.Method);
        Assert.Equal("Size", invocation.Method.Name);
        Assert.Empty(invocation.Arguments);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public void NonLiteralAndOversizedLengthsRemainUnknownRatherThanTruncating(string size)
    {
        var model = Build(Source($"var actual = stackalloc int[{size}];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var allocation = Assert.IsType<StackallocExpression>(Assert.Single(Declarations(model)).Initializer);
        var result = Assert.IsType<ConstructedType>(allocation.Type);
        var length = Assert.IsType<UnknownValueType>(result.TypeArguments[1]);

        Assert.Same(model.TypesByFQN["Standard.Int32"], length.Domain);
    }

    private static LocalDeclarationStatement[] Declarations(ProgramModelClass model)
    {
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var body = probe.MethodsList.Single(method => method.Name == "Run").Body;
        Assert.NotNull(body);

        return body.Statements.OfType<LocalDeclarationStatement>().ToArray();
    }

    private static string Source(string statements)
    {
        return $$"""
            namespace Probe;
            public class Probe
            {
                public static void Run()
                {
                    {{statements}}
                }

                public static int Size() { return 2; }
            }
            """;
    }
}