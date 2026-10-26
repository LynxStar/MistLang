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
        if(typeName.LastIndexOf('.') <= 0)
        {
            error.WriteLine($"error: Could not resolve type '{typeName}'. Provide a fully-qualified CLR type name (e.g., System.Text.Encoding).");
            return 1;
        }

        var diagnostics = new DiagnosticBag(operation ?? new DiagnosticOperation());
        using var provider = AmbientTypeProviderV2Factory.Create(targetFramework, additionalAssemblyPaths, diagnostics);

        if(diagnostics.HasErrors || !ProjectBesideStandard(provider, diagnostics))
        {
            foreach(var diagnostic in diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                if(report is null)
                {
                    error.WriteLine(DiagnosticFormatter.Format(diagnostic));
                }
                else
                {
                    report(diagnostic);
                }
            }

            return 1;
        }

        return Report(provider, typeName, output, error);
    }

    internal static int Report(AmbientTypeProviderV2 provider, string typeName, TextWriter output, TextWriter error)
    {
        if(provider.FindClrType(typeName) is not { } clrType)
        {
            error.WriteLine($"error: Could not resolve type '{typeName}' from any loaded assembly.");
            return 1;
        }

        if(provider.ProjectionOf(clrType) is not DeclaredType projection)
        {
            error.WriteLine($"error: Could not resolve type '{typeName}': it is not projected into Mist.");
            return 1;
        }

        ProjectionInspector.Inspect(provider, typeName, clrType, projection).Write(output);
        return 0;
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