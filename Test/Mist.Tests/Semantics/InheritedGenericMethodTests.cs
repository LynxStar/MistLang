// Tier 3 (Component): what a call to a method inherited from a GENERIC base binds to, asserted on the
// model because emission re-writes the call as spelled and lets Roslyn substitute again. The base's T
// is bound by the derived declaration's base clause and only then by the receiver, so a closed base and
// a renamed one are the two shapes that tell composition from a receiver-only substitution. See
// ProgramModelProbe for the harness.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class InheritedGenericMethodTests : ProgramModelProbe
{
    public InheritedGenericMethodTests()
        : base("MistInheritedGenericMethod_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Container<T>
        {
            public T Value;

            public Container(T value)
            {
                Value = value;
            }

            public T Get()
            {
                return Value;
            }

            public Container<T> With(T replacement)
            {
                return new Container<T>(replacement);
            }
        }

        public struct IntBox : Container<int>
        {
            public int Extra;

            public IntBox(int value, int extra) : base(value)
            {
                Extra = extra;
            }
        }

        public struct Renamed<U> : Container<U>
        {
            public int Tag;

            public Renamed(U value, int tag) : base(value)
            {
                Tag = tag;
            }

            public U Twice()
            {
                return Get();
            }
        }

        public abstract Transformable<T>
        {
            T Transform(T input);
        }

        public class Doubler : Transformable<int>
        {
            public int Transform(int input)
            {
                return input * 2;
            }
        }
        """;

    protected override string Preamble { get; } = """
        var box = new IntBox(42, 7);
        var renamed = new Renamed<string>("r", 1);
        """;

    [Theory]
    [InlineData("var got = box.Get();", "int")]
    [InlineData("var inner = renamed.Get();", "string")]
    [InlineData("var swapped = box.With(9);", "Container<int>")]
    [InlineData("var replaced = renamed.With(\"s\");", "Container<string>")]
    public void AnInheritedMethodAnswersThroughTheBaseClauseAndTheReceiver(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.NotNull(call.Method);
        Assert.Equal(expected, TypeDisplay.Mist(call.Type));
    }

    [Fact]
    public void AConformerDeclarationHidesTheSlotItImplements()
    {
        const string statement = "var doubled = new Doubler().Transform(21);";
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal("Doubler", call.Method?.DeclaringType?.Name);
        Assert.Empty(Errors(BuildRun(statement), "MIST0386"));
    }

    [Fact]
    public void ABareNameInsideTheDerivedTypeReachesTheInheritedMethod()
    {
        var model = BuildModel("var twice = renamed.Twice();", extraTypes: "", extraPreamble: "", out var diagnostics);
        var renamed = Assert.IsType<StructType>(model.TypesByFQN["Probe.Renamed<>"]);
        var twice = renamed.MethodsList.Single(m => m.Name == "Twice");
        var returned = Assert.IsType<ReturnStatement>(twice.Body!.Statements.Single());
        var call = Assert.IsType<InvocationExpression>(returned.Value);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal("Get", call.Method?.Name);
        Assert.Equal("U", TypeDisplay.Mist(call.Type));
    }
}