using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class DerivedCloneEmissionTests : ProgramModelProbe
{
    public DerivedCloneEmissionTests()
        : base("MistDerivedCloneEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Style
        {
            public int Size;
        }

        public struct Margin
        {
            public owned Style Rule;
            public int Inset;
        }

        public owned class Page
        {
            public Style Title;
            public readonly Style Footer;
            public Margin Edge;
            public Style? Note;
        }

        public class Animal
        {
            public owned Style Collar;
        }

        public class Dog : Animal
        {
            public owned Style Leash;
        }

        public class Holder<T>
        {
            public owned T Item;
        }

        public class Unreached
        {
            public int Count;
        }
        """;

    protected override string Preamble { get; } = "";

    private const string Clonable = """
        namespace Standard;

        public abstract IClonable<T>
        {
            owned T Clone();
        }
        """;

    [Fact]
    public void AClassIsCopiedByMemberwiseCloneAndEachOwnedSlotIsReplacedByACopy()
    {
        var emitted = EmitDeclaration("Page", "var shared = new Page(); owned Page mine = shared;");

        Assert.Contains("public Probe.Page Clone()", emitted);
        Assert.Contains("var copy = (Probe.Page)MemberwiseClone();", emitted);
        Assert.Contains("copy.__CloneSlots_Page();", emitted);
        Assert.Contains("this.Title = this.Title?.Clone()!;", emitted);
        Assert.Contains("System.Runtime.CompilerServices.Unsafe.AsRef(in this.Footer) = this.Footer?.Clone()!;", emitted);
        Assert.Contains("this.Edge = this.Edge.Clone();", emitted);
        Assert.Contains("this.Note = (this.Note is { } __present0 ? (Probe.Style?)(__present0?.Clone()!) : default(Probe.Style?));", emitted);
        Assert.Contains(": Standard.IClonable<Probe.Page>", emitted);
    }

    [Fact]
    public void AStructIsCopiedByValueAndItsOwnedFieldReplaced()
    {
        var emitted = EmitDeclaration("Margin", "var shared = new Page(); owned Page mine = shared;");

        Assert.Contains("public Probe.Margin Clone()", emitted);
        Assert.Contains("var copy = this;", emitted);
        Assert.Contains("copy.Rule = copy.Rule?.Clone()!;", emitted);
        Assert.DoesNotContain("copy.Inset =", emitted);
    }

    [Fact]
    public void ASubclassOverridesItsBasesCloneWithItsOwnTypeAndCopiesOnlyItsOwnSlots()
    {
        const string clones = "Animal shared = new Dog(); owned Animal mine = shared;";
        var animal = EmitDeclaration("Animal", clones);
        var dog = EmitDeclaration("Dog", clones);

        Assert.Contains("public virtual Probe.Animal Clone()", animal);
        Assert.Contains("var copy = (Probe.Animal)MemberwiseClone();", animal);
        Assert.Contains("protected void __CloneSlots_Animal()", animal);
        Assert.Contains("public override Probe.Dog Clone()", dog);
        Assert.Contains("var copy = (Probe.Dog)base.Clone();", dog);
        Assert.Contains("copy.__CloneSlots_Dog();", dog);
        Assert.DoesNotContain("copy.__CloneSlots_Animal();", dog);
    }

    [Fact]
    public void AnOwnedSlotOfATypeParameterIsCopiedThroughItsArgumentsConformance()
    {
        var emitted = EmitDeclaration("Holder<>", "var shared = new Holder<Style>(); owned Holder<Style> mine = shared;");

        Assert.Contains("this.Item = (this.Item is Standard.IClonable<T> __clonable0 ? __clonable0.Clone() : this.Item);", emitted);
        Assert.Contains("public Probe.Style Clone()", EmitDeclaration("Style", "var shared = new Holder<Style>(); owned Holder<Style> mine = shared;"));
    }

    [Fact]
    public void ATypeNothingClonesKeepsItsEmissionWithoutCloneOrConformance()
    {
        var emitted = EmitDeclaration("Unreached", "var shared = new Page(); owned Page mine = shared;");

        Assert.DoesNotContain("Clone", emitted);
        Assert.DoesNotContain("IClonable", emitted);
    }

    private string EmitDeclaration(string typeName, string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics, referenced: Clonable);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        using(CloneDemand.Enter(model))
        {
            TypeEmitter.EmitTypeBody(
                type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);
        }

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}