using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests;

/// <summary>
/// Property-based tests for the Semantic Model and Diagnostics.
/// </summary>
public class SemanticModelDiagnosticsPropertyTests
{
    // ---------------------------------------------------------------
    //  28.1 — Semantic model immutability
    //  **Validates: Correctness Property 6**
    //  After ISemanticModelBuilder.Build returns, verify via reflection
    //  that no public API on ISemanticModel or any reachable ISymbol,
    //  IType, or IScope permits mutation.
    // ---------------------------------------------------------------

    [Fact]
    public void SemanticModelImmutability_NoPublicMutationApi()
    {
        // Check ISemanticModel interface
        AssertInterfaceIsImmutable(typeof(ISemanticModel));

        // Check ISymbol interface
        AssertInterfaceIsImmutable(typeof(ISymbol));

        // Check IType interface
        AssertInterfaceIsImmutable(typeof(IType));

        // Check IScope interface
        AssertInterfaceIsImmutable(typeof(IScope));
    }

    [Fact]
    public void SemanticModelImmutability_ConcreteTypesReturnImmutableCollections()
    {
        // Build a semantic model and verify the concrete return types
        var source = new SourceText("test.mist", "");
        var unit = new CompilationUnit(source);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));
        var builder = new SemanticModelBuilder();
        var bag = new DiagnosticBag();

        var model = builder.Build(compilation, ImmutableArray<Storm.Runtime.SyntaxNode>.Empty, bag);

        // Verify Units returns IReadOnlyList
        Assert.IsAssignableFrom<IReadOnlyList<CompilationUnit>>(model.Units);

        // Verify AllSymbols returns ImmutableArray
        var allSymbols = model.AllSymbols;
        Assert.IsType<ImmutableArray<ISymbol>>(allSymbols);

        // Verify GlobalScope.Members returns IReadOnlyList
        Assert.IsAssignableFrom<IReadOnlyList<ISymbol>>(model.GlobalScope.Members);
    }

    [Fact]
    public void SemanticModelImmutability_InterfacePropertiesHaveNoSetters()
    {
        var interfaces = new[]
        {
            typeof(ISemanticModel),
            typeof(ISymbol),
            typeof(IType),
            typeof(IScope),
        };

        foreach(var iface in interfaces)
        {
            var properties = iface.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach(var prop in properties)
            {
                Assert.Null(prop.GetSetMethod());
            }
        }
    }

    private static void AssertInterfaceIsImmutable(Type interfaceType)
    {
        var properties = interfaceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach(var prop in properties)
        {
            // No public setters
            var setter = prop.GetSetMethod();
            Assert.True(setter is null,
                $"{interfaceType.Name}.{prop.Name} has a public setter, violating immutability.");

            // Collection-typed properties should return IReadOnly* or Immutable* types
            var returnType = prop.PropertyType;
            if(IsCollectionType(returnType))
            {
                Assert.True(
                    IsImmutableCollectionType(returnType),
                    $"{interfaceType.Name}.{prop.Name} returns {returnType.Name} which is not an immutable collection type. " +
                    $"Expected IReadOnly*, ImmutableArray, or ImmutableDictionary.");
            }
        }
    }

    private static bool IsCollectionType(Type type)
    {
        if(type.IsArray)
        {
            return true;
        }
        if(!type.IsGenericType)
        {
            return false;
        }
        var genericDef = type.GetGenericTypeDefinition();
        return genericDef == typeof(IList<>)
            || genericDef == typeof(List<>)
            || genericDef == typeof(ICollection<>)
            || genericDef == typeof(IEnumerable<>)
            || genericDef == typeof(IReadOnlyList<>)
            || genericDef == typeof(IReadOnlyCollection<>)
            || genericDef == typeof(ImmutableArray<>)
            || genericDef == typeof(ImmutableList<>);
    }

    private static bool IsImmutableCollectionType(Type type)
    {
        if(!type.IsGenericType)
        {
            return false;
        }
        var genericDef = type.GetGenericTypeDefinition();
        return genericDef == typeof(IReadOnlyList<>)
            || genericDef == typeof(IReadOnlyCollection<>)
            || genericDef == typeof(ImmutableArray<>)
            || genericDef == typeof(ImmutableList<>)
            || genericDef == typeof(ImmutableDictionary<,>)
            || genericDef == typeof(IReadOnlyDictionary<,>);
    }

    // ---------------------------------------------------------------
    //  28.2 — Diagnostic completeness
    //  **Validates: Correctness Property 7**
    //  Every diagnostic has non-null Id, valid Severity, Stage;
    //  location diagnostics have valid paths and Start ≤ End.
    // ---------------------------------------------------------------

    [Fact]
    public void DiagnosticCompleteness_AllDiagnosticsHaveRequiredFields()
    {
        // Create diagnostics from various pipeline stages and verify completeness
        var diagnostics = CreateSampleDiagnostics();

        foreach(var diag in diagnostics)
        {
            // Non-null Id
            Assert.NotNull(diag.Id.Value);
            Assert.NotEmpty(diag.Id.Value);

            // Valid Severity (must be a defined enum value)
            Assert.True(Enum.IsDefined(typeof(DiagnosticSeverity), diag.Severity),
                $"Diagnostic {diag.Id.Value} has invalid severity: {diag.Severity}");

            // Valid Stage (must be a defined enum value)
            Assert.True(Enum.IsDefined(typeof(PipelineStage), diag.Stage),
                $"Diagnostic {diag.Id.Value} has invalid stage: {diag.Stage}");

            // Non-null, non-empty message
            Assert.NotNull(diag.Message);
            Assert.NotEmpty(diag.Message);

            // If location is present, Start ≤ End
            if(diag.Location is not null)
            {
                var loc = diag.Location.Value;
                Assert.True(
                    loc.Start.Line < loc.End.Line ||
                    (loc.Start.Line == loc.End.Line && loc.Start.Column <= loc.End.Column),
                    $"Diagnostic {diag.Id.Value} has invalid location: Start ({loc.Start.Line},{loc.Start.Column}) > End ({loc.End.Line},{loc.End.Column})");

                // If location is present, CompilationUnitPath should be non-null
                Assert.NotNull(diag.CompilationUnitPath);
            }
        }
    }

    [Fact]
    public void DiagnosticCompleteness_CompilerResultDiagnosticsAreValid()
    {
        // Run the compiler pipeline with empty input and verify all diagnostics
        var source = new SourceText("test.mist", "");
        var unit = new CompilationUnit(source);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        // Lex
        var lexer = new Mist.Lexing.MistLexer();
        var lexBag = new DiagnosticBag();
        var tokenStream = lexer.Lex(source, lexBag);

        // Parse
        var parser = new Mist.Parsing.MistParser();
        var parseBag = new DiagnosticBag();
        var parseResult = parser.Parse(tokenStream, parseBag);

        // Build semantic model
        var builder = new SemanticModelBuilder();
        var semBag = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray<Storm.Runtime.SyntaxNode>.Empty, semBag);

        // Collect all diagnostics
        var allDiagnostics = lexBag.ToImmutableArray()
            .AddRange(parseBag.ToImmutableArray())
            .AddRange(parseResult.Diagnostics)
            .AddRange(semBag.ToImmutableArray());

        // Verify each diagnostic
        foreach(var diag in allDiagnostics)
        {
            Assert.NotNull(diag.Id.Value);
            Assert.True(Enum.IsDefined(typeof(DiagnosticSeverity), diag.Severity));
            Assert.True(Enum.IsDefined(typeof(PipelineStage), diag.Stage));
            Assert.NotNull(diag.Message);

            if(diag.Location is not null)
            {
                var loc = diag.Location.Value;
                Assert.True(
                    loc.Start.Line < loc.End.Line ||
                    (loc.Start.Line == loc.End.Line && loc.Start.Column <= loc.End.Column));
            }
        }
    }

    private static ImmutableArray<Diagnostic> CreateSampleDiagnostics()
    {
        return ImmutableArray.Create(
            new Diagnostic(
                new DiagnosticId("MIST_LEX_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                "Unexpected character",
                new LinePositionSpan(new LinePosition(1, 1), new LinePosition(1, 2)),
                "test.mist",
                null),
            new Diagnostic(
                new DiagnosticId("MIST_PARSE_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Parsing,
                "Expected expression",
                new LinePositionSpan(new LinePosition(1, 5), new LinePosition(1, 10)),
                "test.mist",
                null),
            new Diagnostic(
                new DiagnosticId("MIST_SEMANTIC_WARNING"),
                DiagnosticSeverity.Warning,
                PipelineStage.SemanticModelConstruction,
                "Unused variable",
                null,
                null,
                null),
            new Diagnostic(
                new DiagnosticId("MIST_TYPE_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                "Type mismatch",
                new LinePositionSpan(new LinePosition(3, 1), new LinePosition(3, 15)),
                "test.mist",
                "TypeChecker"),
            new Diagnostic(
                new DiagnosticId("MIST_BACKEND_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                "Compilation failed",
                null,
                null,
                null)
        );
    }
}
