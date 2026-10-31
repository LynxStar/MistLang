using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Driver;

internal sealed class TranslationStage(ILexer lexer, IParser parser, ICompilationProgressReporter progressReporter)
{
    public MistCompilation Run(
        IBackend backend, ImmutableArray<ClaimedInput> inputs, MistCompilation compilation,
        PipelineInput pipelineInput, IAmbientTypeSource ambientTypes)
    {
        var stopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("3.5", "Translation");
        var diagnostics = pipelineInput.Diagnostics;
        var options = pipelineInput.Options;
        var units = compilation.Units.ToBuilder();
        var translatedCount = 0;
        var project = new ProjectFacts(options.NamespaceRoot, options.ProjectFolder, compilation.GlobalUsings);
        var parsedUnits = compilation.Units.Where(unit => !unit.IsStdlib).ToImmutableArray();
        foreach(var translator in backend.SourceTranslators)
        {
            var claimed = inputs.Where(input => translator.Extensions.Contains(
                Path.GetExtension(input.Text.Path), StringComparer.OrdinalIgnoreCase)).ToImmutableArray();
            var translated = translator.Translate(new TranslationContext(
                claimed, parsedUnits, ambientTypes, lexer, parser, project, diagnostics));
            foreach(var unit in translated)
            {
                translatedCount++;
                var relative = Path.GetRelativePath(unit.Project.ProjectFolder, unit.Text.Path);
                var rootProject = Path.GetFullPath(unit.Project.ProjectFolder) == Path.GetFullPath(options.ProjectFolder);
                var projectName = rootProject ? options.ProjectName : unit.Project.Name;
                var outputRoot = Path.Combine(options.OutputDirectory, projectName);
                var generatedPath = Path.GetFullPath(Path.Combine(outputRoot, "translated", relative + ".mist"));
                SourceText source;
                try
                {
                    _ = Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
                    File.WriteAllText(generatedPath, unit.Text.Contents);
                    source = new SourceText(generatedPath, unit.Text.Contents);
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    diagnostics.ReportTyped(DriverDiagnostics.FileError, new FileOnly(generatedPath),
                        new PathValue(generatedPath), new TextValue(ex.Message));
                    continue;
                }

                var parsed = Parse(source, diagnostics);
                units.Add(new CompilationUnit(source, unit.Project.Name, unit.Project.ProjectFolder)
                {
                    OriginProjectPath = rootProject ? null : unit.Project.CanonicalTomlPath,
                    SourcePath = unit.Text.Path,
                    Ast = parsed,
                });
            }
        }

        progressReporter.ReportStageComplete("3.5", stopwatch.Elapsed,
            [$"{inputs.Length} claimed inputs", $"{translatedCount} translated units"]);
        return compilation with { Units = units.ToImmutable() };
    }

    private Storm.Runtime.SyntaxNode? Parse(SourceText source, DiagnosticBag diagnostics)
    {
        var bag = new DiagnosticBag(diagnostics.Operation);
        var tokens = lexer.Lex(source, bag);
        Storm.Runtime.SyntaxNode? root = null;
        if(!bag.HasErrors)
        {
            var parsed = parser.Parse(tokens, bag);
            foreach(var diagnostic in parsed.Diagnostics)
            {
                bag.Add(diagnostic);
            }

            if(!bag.HasErrors && parsed.Root is { } ast)
            {
                ast.SetParentLinks();
                root = ast;
            }
        }

        foreach(var diagnostic in bag.ToImmutableArray())
        {
            diagnostics.Add(diagnostic);
        }

        return root;
    }
}