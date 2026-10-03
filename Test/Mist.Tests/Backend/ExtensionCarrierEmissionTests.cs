// Tier 3 (Component): what an extension method's carrier declares and what a call to it writes. Asserted on
// the emitted text, since C# takes all of a carrier's type arguments or none, and a call that lets C# infer
// what Mist stated can pick a different argument and still print something plausible.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ExtensionCarrierEmissionTests
{
    private const string Types = """
        public class Animal { }
        public class Dog : Animal { }
        public abstract IShape { int Sides(); }
        public class Square : IShape { public int Sides() { return 4; } }
        public class Tag { public Tag(string text) { } }
        public class Box<T> { }
        public class Holder { }

        <T> extension(Box<T> self)
        {
            public int Put<U>(U item) where U : T { return 1; }
        }

        extension(Holder self)
        {
            public int Count<U>(U shape) where U : IShape { return shape.Sides(); }
            public U Fresh<U>() where U : new() { return new U(); }
            public U Make<U>(string text) where U : new(string) { return new U(text); }
            public int Plus<N:int>(int value) { return value + N; }
            public U Blank<U>() where U : default { U made = default; return made; }
        }
        """;

    private const string Calls = """
        var box = new Box<Animal>();
        var stated = box.Put<Dog>(new Dog());
        var inferred = box.Put(new Dog());
        var widened = box.Put<Animal>(new Dog());
        func<Dog, int> grouped = box.Put;
        """;

    private static readonly Lazy<(string Calls, string Carriers)> Emitted = new(Emit);

    [Theory]
    [InlineData("int stated = Probe.BoxExtensions_Probe_0.Put<Probe.Animal, Probe.Dog>(box, new Probe.Dog());")]
    [InlineData("int inferred = Probe.BoxExtensions_Probe_0.Put<Probe.Animal, Probe.Dog>(box, new Probe.Dog());")]
    [InlineData("int widened = Probe.BoxExtensions_Probe_0.Put<Probe.Animal, Probe.Animal>(box, new Probe.Dog());")]
    [InlineData("System.Func<Probe.Dog, int> grouped = box.Put<Probe.Animal, Probe.Dog>;")]
    public void AGenericBlocksMethodCalledWithItsOwnTypeArguments_IsWrittenTheBlocksToo(string written)
    {
        Assert.Contains(written, Emitted.Value.Calls);
    }

    [Theory]
    [InlineData("public static int Put<T, U>(this Probe.Box<T> self, U item)", "where U : T")]
    [InlineData("public static int Count<U>(this Probe.Holder self, U shape)", "where U : Probe.IShape")]
    [InlineData("public static U Fresh<U>(this Probe.Holder self, System.Func<U> __construct_U_0)", "where U : new()")]
    [InlineData("public static U Make<U>(this Probe.Holder self, System.Func<string, U> __construct_U_0, string text)", "where U : Mist.Emission.CSharp.__IConstruct_string<U>")]
    [InlineData("public static int Plus<TN>(this Probe.Holder self, int value)", "where TN : IConstInt")]
    [InlineData("public static U Blank<U>(this Probe.Holder self, System.Func<U> __default_U)", "{")]
    public void AnExtensionMethodsConstraints_AreDeclaredOnItsCarrier(string signature, string next)
    {
        var lines = Emitted.Value.Carriers.Split('\n').Select(line => line.Trim()).ToList();
        var at = lines.IndexOf(signature);

        Assert.True(at >= 0, $"no carrier line '{signature}' in:\n{Emitted.Value.Carriers}");
        Assert.Equal(next, lines[at + 1]);
    }

    private static (string Calls, string Carriers) Emit()
    {
        var model = StandardModelProbe.Build(Calls, out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = new DiagnosticBag();
        var calls = new CSharpWriter();
        var probe = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Probe"]);

        TypeEmitter.EmitTypeBody(probe, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, calls, diagnostics);

        var carriers = new CSharpWriter();
        var anyEmitted = false;

        foreach(var block in model.Assemblies.SelectMany(assembly => assembly.CompilationUnits).SelectMany(unit => unit.Extensions)
            .Where(block => block.ReceiverParameterName == "self" && block.MethodsList.Exists(method => method.TypeParametersList.Count > 0)))
        {
            ExtensionBlockEmitter.Emit(block, carriers, diagnostics, ref anyEmitted);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return (calls.ToString(), carriers.ToString());
    }
}