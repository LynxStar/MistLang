// Feature: dependent-type-refinement-constraints, Property 4: Unresolved Constraint Rejection
//
// **Validates: Requirements 2.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that named identifier constraints which do not resolve
/// to any known type produce the MIST_DT_0002 diagnostic.
///
/// <b>Property 4: Unresolved Constraint Rejection</b>
///
/// For all <c>NamedConstraintDependentTypeParameter</c> nodes where the identifier does not
/// resolve to any known type, the SemanticModelBuilder emits an error diagnostic with ID
/// <c>MIST_DT_0002</c> (constraint type not found in scope).
///
/// <b>Validates: Requirements 2.3</b>
/// </summary>
public class UnresolvedConstraintRejectionPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var buildDiag = new DiagnosticBag();
        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        return buildDiag.ToImmutableArray();
    }

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record UnresolvedConstraintScenario(
        string ClassName,
        string ParamName,
        string ConstraintName)
    {
        public override string ToString() =>
            $"class {ClassName}<{ParamName}:{ConstraintName}>";
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Class names that won't conflict with the made-up constraint names.
    /// </summary>
    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper"];

    /// <summary>
    /// Parameter names for the dependent type parameter.
    /// </summary>
    private static readonly string[] ParamNames =
        ["Size", "Count", "Capacity", "Length", "Width"];

    /// <summary>
    /// Made-up type names that do not exist in any default scope.
    /// These will not resolve to any known type.
    /// </summary>
    private static readonly string[] NonExistentTypeNames =
    [
        "NonExistentType",
        "FakeConstraint",
        "Undefined",
        "MissingType",
        "NoSuchType",
        "Phantom",
        "Imaginary",
        "GhostType",
        "BogusConstraint",
        "UnknownRefine",
    ];

    private static Gen<UnresolvedConstraintScenario> GenUnresolvedConstraintScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               from constraintName in Gen.Elements(NonExistentTypeNames)
               select new UnresolvedConstraintScenario(className, paramName, constraintName);
    }

    public static Arbitrary<UnresolvedConstraintScenario> ArbitraryUnresolvedConstraintScenario() =>
        GenUnresolvedConstraintScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 4: For all NamedConstraintDependentTypeParameter nodes where the identifier
    /// does not resolve to any known type, the SemanticModelBuilder emits MIST_DT_0002
    /// (constraint type not found in scope).
    ///
    /// <b>Validates: Requirements 2.3</b>
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnresolvedConstraintRejectionPropertyTests) })]
    public bool UnresolvedConstraint_EmitsMistDt0002(UnresolvedConstraintScenario scenario)
    {
        // Build source: class ClassName<ParamName:NonExistentConstraint> { }
        var source = $"class {scenario.ClassName}<{scenario.ParamName}:{scenario.ConstraintName}> {{ }}";

        var diagnostics = CompileAndGetDiagnostics(source);

        // Verify MIST_DT_0002 is emitted
        var dt0002Diagnostics = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.DependentTypeConstraintNotFound)
            .ToList();

        return dt0002Diagnostics.Count >= 1
            && dt0002Diagnostics.Any(d => d.Message.Contains(scenario.ConstraintName));
    }
}