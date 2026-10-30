using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Inspection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

public static class TypeInspector
{
    public static int Inspect(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        string typeName,
        TextWriter output,
        DiagnosticOperation? operation = null,
        Action<Diagnostic>? report = null)
    {
        return Inspect(targetFramework, additionalAssemblyPaths, typeName, output, Console.Error, operation, report);
    }

    internal static int Inspect(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        string typeName,
        TextWriter output,
        TextWriter error,
        DiagnosticOperation? operation = null,
        Action<Diagnostic>? report = null)
    {
        var diagnostics = new DiagnosticBag(operation ?? new DiagnosticOperation());
        var receive = report ?? (diagnostic => error.WriteLine(DiagnosticFormatter.Format(diagnostic)));
        if(typeName.LastIndexOf('.') <= 0)
        {
            diagnostics.ReportTyped(CommandDiagnostics.InspectTypeNotQualified, new Absent(AbsenceReason.Unavailable), new NameValue(typeName));
            _ = Drain(diagnostics.Admitted, receive);
            return 1;
        }

        using var provider = AmbientTypeProviderV2Factory.Create(targetFramework, additionalAssemblyPaths, diagnostics);
        var projected = !diagnostics.HasErrors && ProjectBesideStandard(provider, diagnostics);
        var failed = Drain(diagnostics.Admitted, receive);
        return !projected || failed ? 1 : Report(provider, typeName, output, error, diagnostics, receive);
    }

    internal static int Report(
        AmbientTypeProviderV2 provider, string typeName, TextWriter output, TextWriter error,
        DiagnosticBag? diagnostics = null, Action<Diagnostic>? receive = null)
    {
        diagnostics ??= new DiagnosticBag();
        receive ??= diagnostic => error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        var reported = diagnostics.Admitted.Count;
        var failed = false;
        if(provider.FindClrType(typeName) is not { } clrType)
        {
            diagnostics.ReportTyped(CommandDiagnostics.InspectTypeNotFound, new Absent(AbsenceReason.Unavailable), new NameValue(typeName));
            failed = true;
        }
        else if(provider.ProjectionOf(clrType) is not DeclaredType projection)
        {
            diagnostics.ReportTyped(CommandDiagnostics.InspectTypeNotProjected,
                new ExternalLocation(clrType.Assembly.GetName().Name ?? "", clrType.FullName ?? clrType.Name), new NameValue(typeName));
            failed = true;
        }
        else
        {
            ProjectionInspector.Inspect(provider, typeName, clrType, projection).Write(output);
        }

        var hasErrors = Drain(diagnostics.Admitted.Skip(reported), receive);
        return failed || hasErrors ? 1 : 0;
    }

    internal static bool Drain(IEnumerable<Diagnostic> diagnostics, Action<Diagnostic> receive)
    {
        var failed = false;
        foreach(var diagnostic in diagnostics)
        {
            receive(diagnostic);
            failed |= diagnostic.Severity == DiagnosticSeverity.Error;
        }

        return failed;
    }

    /// <summary>
    /// The provider projects only into a model that already holds Standard, because every CLR type
    /// Standard aliases resolves to Standard's own declaration. Standard alone is the smallest such
    /// model, and nothing else a build adds to it changes how a CLR member is projected.
    /// </summary>
    internal static bool ProjectBesideStandard(AmbientTypeProviderV2 provider, DiagnosticBag diagnostics)
    {
        var units = StdlibLoader.LoadFor(diagnostics);
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parsedUnits = ImmutableArray.CreateBuilder<CompilationUnit>(units.Length);

        foreach(var unit in units)
        {
            if(parser.Parse(lexer.Lex(unit.Source, diagnostics), diagnostics).Root is not { } root)
            {
                return false;
            }

            root.SetParentLinks();
            parsedUnits.Add(unit with { Ast = root });
        }

        if(diagnostics.HasErrors)
        {
            return false;
        }

        var standardRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
        var options = new CompilerOptions(
            ProjectFolder: standardRoot,
            SourcePaths: [],
            Backend: BackendId.CSharp,
            OutputDirectory: standardRoot,
            BinDirectory: standardRoot,
            ProjectName: "Inspect",
            NamespaceRoot: "Inspect");

        var compilation = new MistCompilation(parsedUnits.MoveToImmutable()) { HasStandardLibrary = true };
        return ProgramModelBuilder.Build(compilation, options, diagnostics, provider) is not null
            && !diagnostics.HasErrors;
    }
}