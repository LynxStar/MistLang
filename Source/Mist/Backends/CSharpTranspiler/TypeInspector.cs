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
        TextWriter output)
    {
        return Inspect(targetFramework, additionalAssemblyPaths, typeName, output, Console.Error);
    }

    internal static int Inspect(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        string typeName,
        TextWriter output,
        TextWriter error)
    {
        if(typeName.LastIndexOf('.') <= 0)
        {
            error.WriteLine($"error: Could not resolve type '{typeName}'. Provide a fully-qualified CLR type name (e.g., System.Text.Encoding).");
            return 1;
        }

        var diagnostics = new DiagnosticBag();
        using var provider = AmbientTypeProviderV2Factory.Create(targetFramework, additionalAssemblyPaths, diagnostics);

        if(diagnostics.HasErrors || !ProjectBesideStandard(provider, diagnostics))
        {
            foreach(var diagnostic in diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                error.WriteLine(DiagnosticFormatter.Format(diagnostic));
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
        var standardRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
        var units = StdlibLoader.LoadFrom(standardRoot, diagnostics);
        var lexer = new MistLexer();
        var parser = new MistParser();
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>(units.Length);

        foreach(var unit in units)
        {
            if(parser.Parse(lexer.Lex(unit.Source, diagnostics), diagnostics).Root is not { } root)
            {
                return false;
            }

            asts.Add(root);
        }

        if(diagnostics.HasErrors)
        {
            return false;
        }

        var options = new CompilerOptions(
            ProjectFolder: standardRoot,
            SourcePaths: [],
            Backend: new BackendId("csharp"),
            OutputDirectory: standardRoot,
            BinDirectory: standardRoot,
            ProjectName: "Inspect",
            NamespaceRoot: "Inspect",
            EnabledAnalyzerIds: []);

        var compilation = new MistCompilation(units) { HasStandardLibrary = true };
        return ProgramModelBuilder.Build(compilation, asts.MoveToImmutable(), options, diagnostics, provider) is not null
            && !diagnostics.HasErrors;
    }
}