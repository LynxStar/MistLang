using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// What a call on a value binds to. Asserted on the model because emission writes the call as source
/// spelled it and lets Roslyn resolve it again, so a program that runs says nothing about whether the
/// binder judged the callee.
/// </summary>
[Trait("Tier", "3")]
public sealed class CallabilityTests
{
    private const string NotCallable = "MIST_PM_NOT_CALLABLE";

    private const string Holder = """
        public class Holder
        {
            public int Number;
            public string Label { get; set; }
            public static int Shared;
            public func<int, int> Handler;
            public func<int, int> Transform { get; set; }
            public static func<int, int> SharedHandler = x => x;

            public Holder()
            {
                this.Number = 1;
                this.Label = "label";
                this.Handler = x => x;
                this.Transform = x => x;
            }

            public int GetNumber()
            {
                return this.Number;
            }

            public int CallsItsOwnHandlers()
            {
                return Handler(1) + Transform(1);
            }

            public static int CallsItsParameter(int count, func<int, int> handler)
            {
                return handler(count);
            }
        }

        extension(Holder self)
        {
            public string Label(int repeat)
            {
                return "extension";
            }
        }
        """;

    [Theory]
    [InlineData("var result = holder.Number(1);", "'Number' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    [InlineData("var result = Holder.Shared(1);", "'Shared' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    [InlineData("int count = 3; var result = count(1);", "'count' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    [InlineData("var result = holder.GetNumber()(1);", "A value of type 'int' is not a function, so it cannot be called.")]
    [InlineData("Holder? maybe = holder; var result = maybe?.Number(1);", "'Number' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    public void AValueWhoseTypeIsNotAFunctionIsNotCalled(string statements, string message)
    {
        var diagnostic = Assert.Single(Diagnostics($"var holder = new Holder(); {statements}"), d => d.Severity == DiagnosticSeverity.Error);

        Assert.Equal((NotCallable, message), (diagnostic.Id.Value, diagnostic.Message));
    }

    [Theory]
    [InlineData("public class Bare { public int Number; public int Run() { return Number(1); } }", "'Number'")]
    [InlineData("public class Bare { public int Size { get; set; } public int Run() { return Size(1); } }", "'Size'")]
    [InlineData("public class Bare { public static int Run(int count) { return count(1); } }", "'count'")]
    public void AMemberOrParameterNamedBareIsJudgedTheSameWay(string declaration, string named)
    {
        var diagnostic = Assert.Single(Diagnostics("int unused = 0;", declaration), d => d.Severity == DiagnosticSeverity.Error);

        Assert.Equal(NotCallable, diagnostic.Id.Value);
        Assert.Contains(named, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var result = holder.Handler(1);")]
    [InlineData("var result = holder.Transform(1);")]
    [InlineData("var result = Holder.SharedHandler(1);")]
    [InlineData("func<int, int> handler = x => x; var result = handler(1);")]
    [InlineData("var result = Holder.CallsItsParameter(1, x => x);")]
    [InlineData("var result = holder.CallsItsOwnHandlers();")]
    public void AFunctionTypedValueIsCalledForItsResult(string statements)
    {
        var call = LastInitializer($"var holder = new Holder(); {statements}", Holder, ambient: null, out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Int32", Assert.IsType<InvocationExpression>(call).Type.Name);
    }

    [Theory]
    [InlineData("func<int, int>? handler = x => x; var result = handler(1);")]
    [InlineData("Holder? maybe = holder; var result = maybe?.Handler(1);")]
    public void AnOptionalCallableIsLeftToTheRuleAboutOptionals(string statements)
    {
        var diagnostics = Diagnostics($"var holder = new Holder(); {statements}");

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AnExtensionMethodAnswersTheCallAnUncallableMemberCannot()
    {
        var call = Assert.IsType<InvocationExpression>(LastInitializer(
            "var holder = new Holder(); var result = holder.Label(2);", Holder, ambient: null, out var diagnostics));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("Label", 1), (call.Method?.Name, call.Method?.ParametersList.Count));
        Assert.Equal("String", call.Type.Name);
    }

    [Fact]
    public void AClrPropertyIsNotCalledAndAnUnimportedExtensionIsNamed()
    {
        using var ambient = ClrAmbient();

        var diagnostics = Diagnostics(
            """
            var numbers = new List<int>();
            var builder = new System.Text.StringBuilder();
            var count = numbers.Count();
            var length = builder.Length(2);
            """,
            "using System.Collections.Generic;",
            ambient);

        Assert.Equal(
            [
                "'Count' is not a method, and its type 'int' is not a function type, so it cannot be called. An extension method named 'Count' is declared in 'System.Linq', which is not imported here.",
                "'Length' is not a method, and its type 'int' is not a function type, so it cannot be called.",
            ],
            diagnostics.Where(d => d.Id.Value == NotCallable).Select(d => d.Message));
    }

    [Fact]
    public void AnImportedClrExtensionAnswersTheCallAClrPropertyCannot()
    {
        using var ambient = ClrAmbient();

        var call = Assert.IsType<InvocationExpression>(LastInitializer(
            "var numbers = new List<int>(); var count = numbers.Count();",
            "using System.Collections.Generic; using System.Linq;",
            ambient,
            out var diagnostics));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Count", call.Method?.Name);
        Assert.Equal("Int32", call.Type.Name);
    }

    [Fact]
    public void AClrDelegateIsCalled()
    {
        using var ambient = ClrAmbient();

        var diagnostics = Diagnostics(
            """
            var holder = new ClrHolder();
            Predicate<int> positive = x => x > 0;
            var named = positive(1);
            var result = holder.Callback(1);
            """,
            """
            using System;

            public class ClrHolder
            {
                public Func<int, int> Callback { get; set; }

                public ClrHolder()
                {
                    this.Callback = x => x;
                }
            }
            """,
            ambient);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    private static AmbientTypeProviderV2 ClrAmbient()
    {
        var diagnostics = new DiagnosticBag();
        var ambient = AmbientTypeProviderV2Factory.Create("net10.0", [], diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return ambient;
    }

    private static ImmutableArray<Diagnostic> Diagnostics(
        string statements, string declarations = Holder, IAmbientTypeProvider? ambient = null)
    {
        _ = StandardModelProbe.Build(statements, out var diagnostics, declarations, ambient);
        return diagnostics;
    }

    private static Expression? LastInitializer(
        string statements, string declarations, IAmbientTypeProvider? ambient, out ImmutableArray<Diagnostic> diagnostics)
    {
        var model = StandardModelProbe.Build(statements, out diagnostics, declarations, ambient);
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var body = Assert.IsType<Block>(probe.MethodsList.Single(method => method.Name == "Run").Body);

        return body.Statements.OfType<LocalDeclarationStatement>().Last().Initializer;
    }
}