// Tier 3 (Component): what a `with` clause and the `with` declaration forms bind and emit. Two of these
// pin bugs that came from one code path being quietly plainer than another, and neither was visible
// until a name failed to resolve several statements away from the cause.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StructuredContextTests : ProgramModelProbe
{
    public StructuredContextTests()
        : base("MistStructuredContext_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract Shape { string Describe(); }

        public class Circle : Shape
        {
            public string Describe() { return "circle"; }
        }

        public class Consumer
        {
            public static string Get() with string s { return s; }

            public static int Untouched() with string s { return 1; }
        }

        public class Greeter
        {
            public string Greeting;

            public Greeter() with string message { this.Greeting = message; }
        }

        public class Erased
        {
            public static T Get<T>() with T value { return value; }

            public static T Outer<T>() with T value { return Get<T>(); }

            public static string? Optional() { return Get<string?>(); }

            public static func<string?> Reference() { return Get<string?>; }
        }
        """;

    protected override string Preamble { get; } = "";

    /// <summary>
    /// A clause binds like a parameter and differs only in where the value comes from.
    /// </summary>
    [Fact]
    public void AClauseIsReadableInTheBody()
    {
        Assert.Contains(
            "var s = (string)MistContext.Peek<string>();",
            EmitMember("Consumer", "Get"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The clause is what makes the method a CONSUMER, so a transparent intermediate that declares one
    /// and never reads it still has to keep the value reachable.
    /// </summary>
    [Fact]
    public void AClauseTheBodyNeverReadsIsStillEmitted()
    {
        Assert.Contains(
            "MistContext.Peek<string>()",
            EmitMember("Consumer", "Untouched"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A constructor's body was emitted through a plainer entry than a method's, so its clauses were
    /// silently undone -- the name simply did not exist in the generated C#.
    /// </summary>
    [Fact]
    public void AConstructorClauseIsEmittedToo()
    {
        Assert.Contains(
            "var message = (string)MistContext.Peek<string>();",
            EmitType("Greeter"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A call closes the callee's open clause to a key and hands that key over, because the CLR would
    /// erase it: `string?` and `string` are one runtime type and two keys. An intermediate holding the
    /// same open key forwards the parameter it was handed, and a method named as a value closes over it.
    /// </summary>
    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Optional", "Get<string?>(typeof(MistContext.Optional<string>))")]
    [InlineData("Outer", "Get<T>(__context_0)")]
    [InlineData("Reference", "Get<string?>(typeof(MistContext.Optional<string>))")]
    public void ACallHandsTheCalleeTheKeyItClosedItsClauseTo(string member, string expected)
    {
        Assert.Contains(expected, EmitMember("Erased", member), StringComparison.Ordinal);
    }

    /// <summary>
    /// A `with` declaration takes the REST of its block as its body, and that tail is itself a
    /// statement list which may declare another. Binding the tail through a loop that did not look for
    /// them left every declaration after the first unrecognised.
    /// </summary>
    [Theory]
    [InlineData("with var a = \"x\";\nstring r = a;")]
    [InlineData("with string a = \"x\";\nstring r = a;")]
    [InlineData("with var a = \"x\";\nwith int b = 1;\nint r = b;")]
    [InlineData("with var a = \"x\";\nwith int b = 1;\nwith bool c = true;\nbool r = c;")]
    public void EveryWithDeclarationInARunBindsItsName(string statements)
    {
        Assert.Empty(
            Diagnostics(statements).Where(d => d.Id.Value == "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    /// <summary>
    /// The written type is the KEY, which is how variance is controlled: offering a Circle as a Shape
    /// files it where a Shape consumer looks, and reading the value's own type would not.
    /// </summary>
    [Fact]
    public void AWrittenTypeIsTheKeyRatherThanTheValuesOwn()
    {
        var body = BodyIn("with Shape _ = new Circle();");
        var scope = Assert.IsType<WithBlockStatement>(body.Statements.Single());

        Assert.Equal("Probe.Shape", scope.KeyType.FullyQualifiedName);
        Assert.Equal("Probe.Circle", scope.Value.Type.FullyQualifiedName);
    }

    /// <summary>
    /// The written key is the value's target as a typed local's type is: a lambda written there takes the
    /// pure function type it is filed under, and so is judged as one, instead of its natural plain type.
    /// </summary>
    [Fact]
    [Trait("Tier", "3")]
    public void ALambdaWrittenUnderAPureFunctionKeyTakesTheKeysType()
    {
        var body = BodyIn("with Step g = (int x) => x + 1;", "type Step = pure func<int, int>;");
        var scope = Assert.IsType<WithBlockStatement>(body.Statements.Single());

        Assert.True(scope.Value.Type is IFunctionType { IsPure: true }, TypeDisplay.Mist(scope.Value.Type));
    }

    /// <summary>
    /// The discard and push forms scope no name, which is the whole of what distinguishes them from
    /// the named ones.
    /// </summary>
    [Theory]
    [InlineData("with Shape _ = new Circle();")]
    [InlineData("with new Circle();")]
    public void TheNamelessFormsBindNothing(string statement)
    {
        var scope = Assert.IsType<WithBlockStatement>(BodyIn(statement).Statements.Single());

        Assert.Null(scope.Binding);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("int r = Generic.Get<int>();")]
    [InlineData("Box<int> r = new Box<int>();")]
    [InlineData("func<int> r = Generic.Get<int>;")]
    public void ACallRecordsTheKeyItsCalleesClauseReadsThroughTheCallsOwnTypeArguments(string statement)
    {
        const string generics = """
            public class Generic { public static T Get<T>() with T value { return value; } }

            public class Box<T> { public Box() with T value { } }
            """;

        var keys = DeclarationIn(statement, generics).Initializer switch
        {
            InvocationExpression call => call.ContextTypes,
            NewExpression allocation => allocation.ContextTypes,
            FunctionReferenceExpression reference => reference.ContextTypes,
            var other => throw new Xunit.Sdk.XunitException($"Unexpected initializer {other?.GetType().Name}"),
        };

        Assert.Equal("Standard.Int32", Assert.Single(keys).FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void TheNameAClauseBindsIsMarkedAsStandingForAnAmbientValue()
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);
        var consumer = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Consumer"]);
        var returned = Assert.IsType<ReturnStatement>(consumer.MethodsList.Single(method => method.Name == "Get").Body!.Statements.Single());

        Assert.True(Assert.IsType<LocalReferenceExpression>(returned.Value).Local.IsContextBound);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("with var a = \"x\";")]
    [InlineData("with string a = \"x\";")]
    [InlineData("with (var a = \"x\") { }")]
    public void TheNameAWithDeclarationBindsIsMarkedAsStandingForAnAmbientValue(string statement)
    {
        var scope = Assert.IsType<WithBlockStatement>(BodyIn(statement).Statements.Single());

        Assert.True(scope.Binding!.IsContextBound);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AnInferredOptionalReferencePushRetainsItsUnionKey()
    {
        var body = BodyIn("string? absent = void; with absent;");
        var scope = Assert.IsType<WithBlockStatement>(body.Statements.Last());
        var key = Assert.IsType<UnionType>(scope.KeyType);

        Assert.Contains(key.Constituents, type => type.FullyQualifiedName == "Standard.String");
        Assert.Contains(key.Constituents, type => type.FullyQualifiedName == "Standard.Void");
        Assert.Same(scope.Value.Type, scope.KeyType);
    }

    private System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics(string statements)
    {
        _ = BuildModel(statements, extraTypes: "", extraPreamble: "", out var diagnostics);
        return diagnostics;
    }

    private string EmitMember(string typeName, string methodName)
    {
        var emitted = EmitType(typeName);
        var index = emitted.IndexOf(methodName, StringComparison.Ordinal);

        Assert.True(index >= 0, $"{methodName} not emitted:\n{emitted}");

        return emitted[index..];
    }

    private string EmitType(string typeName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);

        return writer.ToString();
    }
}