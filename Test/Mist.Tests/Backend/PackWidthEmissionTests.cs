using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// What a body reading its pack emits where a base list, and no specialization, closed the pack: a
// default restated into a conformer and a member flattened into a deriving struct. The width a pack
// has at one place is one function (Tier 1); the emitted bodies are asserted as text (Tier 3), since a
// pack left in one fails only in Roslyn.
public sealed class PackWidthEmissionTests
{
    private const string Types = """
        public abstract ICounter<params TArgs>
        {
            int Count(params TArgs args) { return TArgs.Length; }
            int Through(Func<TArgs..., int> f, params TArgs args) { return f(args...) + TArgs.Length; }
            int Forwarded(params TArgs args) { return Helper.Width(args...) + 100; }
        }

        public class Helper
        {
            public static int Width<params TArgs>(params TArgs args) { return TArgs.Length; }
        }

        public class Plain : ICounter<int, int> { }
        public class Empty : ICounter { }
        public class Open<params TArgs> : ICounter<TArgs...> { }

        public struct Base<params TArgs>
        {
            public int Seed;
            public int Count() { return TArgs.Length + Seed; }
        }

        public struct Derived : Base<int, string> { }
        """;

    private const string Closings = "var open = new Open<int, string, bool>();";

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build(Closings, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Plain", null, "public int Count((int, int) args)", "return 2;")]
    [InlineData("Plain", null, "public int Through(System.Func<int, int, int> f, (int, int) args)", "return f(args.Item1, args.Item2) + 2;")]
    [InlineData("Plain", null, "public int Forwarded((int, int) args)", "return Probe.Helper.Width_2<int, int>(args) + 100;")]
    [InlineData("Empty", null, "public int Count(System.ValueTuple args)", "return 0;")]
    [InlineData("Empty", null, "public int Through(System.Func<int> f, System.ValueTuple args)", "return f() + 0;")]
    [InlineData("Empty", null, "public int Forwarded(System.ValueTuple args)", "return Probe.Helper.Width_0(args) + 100;")]
    [InlineData("Open", 3, "public int Count((T1, T2, T3) args)", "return 3;")]
    [InlineData("Open", 3, "public int Through(System.Func<T1, T2, T3, int> f, (T1, T2, T3) args)", "return f(args.Item1, args.Item2, args.Item3) + 3;")]
    [InlineData("Open", 3, "public int Forwarded((T1, T2, T3) args)", "return Probe.Helper.Width_3<T1, T2, T3>(args) + 100;")]
    [InlineData("Derived", null, "public readonly int Count()", "return 2 + Seed;")]
    public void AMemberWrittenIntoAnotherType_ReadsThePackAsThatTypesBaseListClosedIt(
        string typeName, int? width, string declaration, string body)
    {
        var lines = Lines(Emit(typeName, width));
        var declared = lines.IndexOf(declaration);

        Assert.True(declared >= 0, declaration);
        Assert.Equal(body, lines[declared + 2]);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("A, B", null, 2)]
    [InlineData("", null, 0)]
    [InlineData("A, Own...", 3, 4)]
    [InlineData("A, Own...", null, null)]
    [InlineData("Own", 3, 3)]
    [InlineData("Own", null, null)]
    [InlineData("A", null, null)]
    public void APacksWidth_IsReadOffWhateverClosedIt(string written, int? specialized, int? expected)
    {
        var own = new TypeParameter("Own", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
        var elements = written.Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Select<string, IMistType>(element => element == "Own..." ? new SpreadType(own) : HandBuiltModel.Class(element))
            .ToList();
        var pack = written switch
        {
            "Own" => own,
            "A" => elements[0],
            _ => new LiftedType(HandBuiltModel.Class("Type"), new TypePackValue(elements)),
        };

        using(specialized is { } width ? Monomorph.For([own], [width])?.Enter() : null)
        {
            Assert.Equal(expected, MonomorphReference.Width(pack));
        }
    }

    private static string Emit(string typeName, int? width)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var declared = model.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);

        using(width is { } fixedWidth ? Monomorph.For(declared.TypeParametersList, [fixedWidth])?.Enter() : null)
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