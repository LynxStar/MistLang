// Tier 3 (Component): what a constructorless, defaultable type emits for its field initializers. Purity
// Requirement 12.1 holds them pure, so nothing observable can show how many times one runs; the emitted text
// can, and this is where the "once" the runtime tests cannot see any more is pinned: each initialized field is
// assigned exactly once, by its C# initializer, which both the implicit constructor and __CreateDefault run.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed partial class DefaultableInitializerEmissionTests : ProgramModelProbe
{
    public DefaultableInitializerEmissionTests()
        : base("MistDefaultableInitializer_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Seeded
        {
            public int Count = 7;
            public string Tag = "t";
            public List<int> Items = new List<int>();
            public int Bare;
        }

        public struct Spot
        {
            public int X = 3;
            public int Y;
        }
        """;

    protected override string Preamble { get; } = "";

    [GeneratedRegex(@"\bCount = 7;")]
    private static partial Regex CountInitialized();

    [GeneratedRegex(@"\bItems = new System\.Collections\.Generic\.List<int>\(\);")]
    private static partial Regex ItemsInitialized();

    [GeneratedRegex(@"\bX = 3;")]
    private static partial Regex XInitialized();

    [Theory]
    [InlineData("Probe.Seeded", "Seeded")]
    [InlineData("Probe.Spot", "Spot")]
    public void AConstructorlessDefaultableType_AssignsEachInitializedFieldOnceAndDefaultsTheRest(string fullyQualifiedName, string name)
    {
        var text = EmitType(fullyQualifiedName);

        Assert.Contains($"public static {name} __CreateDefault()", text, StringComparison.Ordinal);
        Assert.Contains($"return new {name}();", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__default_", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClassInitializer_IsWrittenOnceAsTheFieldsOwn()
    {
        var text = EmitType("Probe.Seeded");

        _ = Assert.Single(CountInitialized().Matches(text));
        _ = Assert.Single(ItemsInitialized().Matches(text));
        Assert.Contains("this.Bare = default(int);", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Count", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Items", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Tag", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructInitializer_IsWrittenOnceAsTheFieldsOwn()
    {
        var text = EmitType("Probe.Spot");

        _ = Assert.Single(XInitialized().Matches(text));
        Assert.Contains("this.Y = default(int);", text, StringComparison.Ordinal);
    }

    private string EmitType(string fullyQualifiedName)
    {
        var model = Build($"using Standard;\nnamespace Probe;\n{Fixtures}", out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<DeclaredType>(model.TypesByFQN[fullyQualifiedName]);
        var writer = new CSharpWriter();
        var bag = new DiagnosticBag();
        var anyEmitted = false;

        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model, bag), model.VariadicUses, writer, bag, ref anyEmitted);

        return writer.ToString();
    }
}