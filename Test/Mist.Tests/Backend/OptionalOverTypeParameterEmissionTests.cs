// Which C# shape a `K?` takes when K is a TYPE PARAMETER. The answer comes from the parameter's
// CONSTRAINT, and nothing downstream can see the choice: under `where K : class` and unconstrained
// alike, C# accepts `K?` as a nullable REFERENCE annotation, so a wrong spelling there compiles,
// warns about nothing and runs identically -- measured. Only the struct-constrained case changes
// behaviour, so samples/GenericOptionals guards one of the three rows and this file guards all of
// them.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class OptionalOverTypeParameterEmissionTests : ProgramModelProbe
{
    public OptionalOverTypeParameterEmissionTests()
        : base("MistOptionalOverTypeParameter_")
    {
    }

    protected override string Fixtures { get; } = """
        public class ValueHolder<K> where K : struct
        {
            public K? Field;

            public K? Get() { return this.Field; }

            public void Put(K? value) { this.Field = value; }
        }

        public class RefHolder<K> where K : class
        {
            public K? Field;
        }

        public class OpenHolder<K>
        {
            public K? Field;
        }

        public class ClosedHolder
        {
            public int? Number;

            public string? Word;
        }
        """;

    protected override string Preamble { get; } = "";

    /// <summary>
    /// The whole of the fix: C# reads `K?` as Nullable of K only under the struct constraint, and
    /// Nullable of K at `ValueHolder&lt;int&gt;` IS the `int?` the closed side spells, so the two
    /// representations coincide and nothing has to convert between them.
    /// </summary>
    [Fact]
    public void AStructConstrainedParameterTakesTheNullableSpelling()
    {
        Assert.Contains("public K? Field;", EmitDeclaration("ValueHolder"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public K? Get()")]
    [InlineData("Put(K? value)")]
    public void TheSpellingReachesEveryPositionNotJustTheField(string expected)
    {
        Assert.Contains(expected, EmitDeclaration("ValueHolder"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A reference kind carries absence as null, so the bare name already is the whole optional and
    /// the annotation would add nothing. The rows below it are what keep this one honest: `K?` would
    /// also compile here, which is why no sample can hold this claim.
    /// </summary>
    [Fact]
    public void AClassConstrainedParameterStaysBare()
    {
        var emitted = EmitDeclaration("RefHolder");

        Assert.Contains("public K Field;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("K? Field", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case with no answer. Bare is WRONG at a value instantiation -- absence is unrepresentable
    /// and a fresh field reads as present -- and `K?` would be no better, C# reading it as an
    /// annotation rather than as a carrier. Pinned as bare so that a carrier arriving here is a
    /// deliberate change to this row rather than an accident.
    /// </summary>
    [Fact]
    public void AnUnconstrainedParameterStaysBareAndIsStillWrong()
    {
        var emitted = EmitDeclaration("OpenHolder");

        Assert.Contains("public K Field;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("K? Field", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unchanged by the constraint arm, and the reason it is asserted here: the arm reaches
    /// <see cref="TypeNameEmitter.IsValueKind"/>, which every optional is spelled through.
    /// </summary>
    [Fact]
    public void AClosedOptionalIsUnaffected()
    {
        var emitted = EmitDeclaration("ClosedHolder");

        Assert.Contains("public int? Number;", emitted, StringComparison.Ordinal);
        Assert.Contains("public string Word;", emitted, StringComparison.Ordinal);
    }

    private string EmitDeclaration(string typeName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var registered = model.TypesByFQN
            .Single(entry => MistType.StripArity(entry.Key) == $"Probe.{typeName}")
            .Value;

        var type = Assert.IsAssignableFrom<MemberType>(registered);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}