using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Integration;

[Trait("Tier", "3")]
public sealed class TranslatedUnitDiagnosticsTests
{
    private const string Folder = "/probe";
    private const string Member = "public unknown count;";

    [Fact]
    public void AModelErrorInAVerbatimRegionIsReportedAtTheOriginalPosition()
    {
        var razor = new SourceText("Components/Counter.razor", "<p>@count</p>\n\n@code {\n    " + Member + "\n}\n");
        var header = "namespace App.Components;\n\npublic class Counter\n{\n        ";
        var footer = "\n}\n";
        var code = razor.Contents.IndexOf("@code", StringComparison.Ordinal);
        var member = razor.Contents.IndexOf(Member, StringComparison.Ordinal);
        var translated = new SourceText("out/translated/Components/Counter.razor.mist", header + Member + footer)
        {
            Origin = new([
                new OriginSegment(0, header.Length, razor, 0, razor.Contents.Length, Verbatim: false),
                new OriginSegment(header.Length, Member.Length, razor, member, Member.Length, Verbatim: true),
                new OriginSegment(header.Length + Member.Length, footer.Length, razor, code, razor.Contents.Length - code, Verbatim: false),
            ]),
        };

        var reported = Assert.Single(Build(translated), diagnostic => diagnostic.Number.Value == "MIST0394");

        var location = Assert.IsType<GeneratedLocation>(reported.Location);
        var mapped = Assert.IsType<SourceRange>(location.Mapped);
        Assert.Equal(translated.Path, location.GeneratedPath);
        Assert.Equal(new LinePosition(5, 9), location.Native.Start);
        Assert.Equal((razor.Path, member), (mapped.Path, mapped.Offset));
        Assert.Equal(new LinePosition(4, 5), mapped.Lines!.Value.Start);
        Assert.StartsWith("Components/Counter.razor(4,5): error MIST0394: ", DiagnosticFormatter.Format(reported));
        Assert.EndsWith(" (translated: out/translated/Components/Counter.razor.mist(5,9))", DiagnosticFormatter.Format(reported, verbose: true));
    }

    private static ImmutableArray<Diagnostic> Build(SourceText source)
    {
        var parseDiagnostics = new DiagnosticBag();
        var parsed = new MistParser().Parse(new MistLexer().Lex(source, parseDiagnostics), parseDiagnostics);
        Assert.NotNull(parsed.Root);
        Assert.Empty(parseDiagnostics.ToImmutableArray());

        var options = new CompilerOptions(
            ProjectFolder: Folder,
            SourcePaths: [source.Path],
            Backend: new BackendId("csharp"),
            OutputDirectory: Folder + "/out",
            BinDirectory: Folder + "/bin",
            ProjectName: "App",
            NamespaceRoot: "App",
            OutputType: ProjectOutputType.Library);
        var diagnostics = new DiagnosticBag();
        _ = ProgramModelBuilder.Build(
            new MistCompilation([new CompilationUnit(source, "App", Folder) { Ast = parsed.Root }], OutputType: ProjectOutputType.Library),
            options,
            diagnostics,
            new StandardTypesAmbientProvider());

        return diagnostics.ToImmutableArray();
    }

    private sealed class StandardTypesAmbientProvider : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            AmbientProviderTestHelper.RegisterStandardTypes(programModel);
            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is DeclaredType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Assembly = MistAssembly.Ambient;
                }
            }
        }
    }
}