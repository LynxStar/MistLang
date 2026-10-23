using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: what a forwarded value pack emits as. The forwarder is the only thing that names its
// destination's width, and it names it symbolically, so the destination is specialized because the
// forwarder was -- asserted on the discovered set and on the emitted text, since a destination left
// out fails only in Roslyn.
[Trait("Tier", "3")]
public sealed class PackForwardingEmissionTests
{
    private const string Types = """
        public class Bundle<params TArgs>
        {
            public Bundle(params TArgs values) { }
        }

        public class Relay<params TArgs>
        {
            public int Source(params TArgs args) { return Calls.Count(args...); }
        }

        public class Calls
        {
            public static int Count<params TArgs>(params TArgs args) { return TArgs.Length; }
            public static int Tagged<params TArgs>(string tag, params TArgs args) { return TArgs.Length; }
            public static int Outer<params TArgs>(params TArgs args) { return Count(args...); }
            public static int OuterTagged<params TArgs>(params TArgs args) { return Tagged("t", args...); }
            public static Bundle<TArgs...> Build<params TArgs>(params TArgs args) { return new Bundle<TArgs...>(args...); }
        }
        """;

    private const string Closings = """
        var three = Calls.Outer(1, "x", true);
        var none = Calls.Outer();
        var tagged = Calls.OuterTagged(1, 2);
        var built = Calls.Build(1, "x");
        var relayed = new Relay<int, int, int, int>().Source(1, 2, 3, 4);
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build(Closings, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Fact]
    public void AForwardedPack_SpecializesItsDestinationAtEveryWidthOfTheForwarder()
    {
        var count = Declared("Calls").MethodsList.Single(method => method.Name == "Count");

        Assert.Empty(Model.Value.VariadicUses.WidthsOf(count));
        Assert.Equal(
            ["0", "3", "4"],
            MonomorphSet.Discover(Model.Value).Methods.WidthsOf(count).Select(widths => string.Join("_", widths)));
    }

    [Fact]
    public void AConstructionInAForwardersBody_IsSpecializedAtTheForwardersWidth()
    {
        Assert.Contains(
            MonomorphSet.Discover(Model.Value).Types,
            entry => entry.Definition.Name == "Bundle<>" && entry.Widths is [2]);
    }

    [Theory]
    [InlineData("public static int Outer_3<T1, T2, T3>((T1, T2, T3) args)", "return Count_3<T1, T2, T3>(args);")]
    [InlineData("public static int Outer_0(System.ValueTuple args)", "return Count_0(args);")]
    [InlineData("public static int OuterTagged_2<T1, T2>((T1, T2) args)", "return Tagged_2<T1, T2>(\"t\", args);")]
    [InlineData("public static Probe.Bundle_2<T1, T2> Build_2<T1, T2>((T1, T2) args)", "return new Probe.Bundle_2<T1, T2>(args);")]
    public void AForwardedPack_IsPassedWholeToTheSpecializationOfItsWidth(string declaration, string forward)
    {
        var lines = Lines(Emit("Calls", widths: null));
        var declared = lines.IndexOf(declaration);

        Assert.True(declared >= 0, declaration);
        Assert.Equal(forward, lines[declared + 2]);
    }

    [Fact]
    public void AForwardFromAVariadicType_NamesTheWidthItsOwnSpecializationFixes()
    {
        Assert.Contains("return Probe.Calls.Count_4<T1, T2, T3, T4>(args);", Lines(Emit("Relay", widths: [4])));
    }

    private static MemberType Declared(string typeName)
    {
        return Model.Value.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);
    }

    private static string Emit(string typeName, IReadOnlyList<int>? widths)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var declared = Declared(typeName);

        using(widths is null ? null : Monomorph.For(declared.TypeParametersList, widths)?.Enter())
        {
            TypeEmitter.EmitTypeBody(
                declared, ConstraintClosureMap.Build(model, diagnostics), MonomorphSet.Discover(model).Methods, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}