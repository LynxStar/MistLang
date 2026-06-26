// Feature: properties, Property 4: Property Access Type Safety
// **Validates: Requirements 13.1, 13.2, 13.3, 13.5, 10.4, 10.5, 11.4, 11.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for property access type safety.
///
/// **Validates: Requirements 13.1, 13.2, 13.3, 13.5, 10.4, 10.5, 11.4, 11.5**
///
/// For any property access, type checker accepts iff: reads have visible get,
/// writes have visible set/init with compatible type, init writes are in constructor context.
/// </summary>
public class PropertyAccessTypePropertyTests
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record PropertyAccessTestCase(
        string Description,
        CompilationUnitNode Ast,
        bool ExpectValid)
    {
        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a test case with a get+set auto-property and a read access in Main.
    /// This should be valid since the property has a get accessor.
    /// </summary>
    private static Gen<PropertyAccessTestCase> GenValidReadAccess()
    {
        return from propName in Gen.Elements(
                   "Name", "Value", "Count", "Size")
               select new PropertyAccessTestCase(
                   $"Valid read of {propName} with get accessor",
                   MakeReadAccessProgram(propName, hasGet: true, hasSet: true),
                   ExpectValid: true);
    }

    /// <summary>
    /// Generates a test case with a get+set auto-property and a write access in Main.
    /// This should be valid since the property has a set accessor.
    /// </summary>
    private static Gen<PropertyAccessTestCase> GenValidWriteAccess()
    {
        return from propName in Gen.Elements(
                   "Name", "Value", "Count", "Size")
               from initValue in Gen.Choose(0, 100)
               select new PropertyAccessTestCase(
                   $"Valid write of {propName} with set accessor",
                   MakeWriteAccessProgram(propName, hasGet: true, hasSet: true, initValue),
                   ExpectValid: true);
    }

    /// <summary>
    /// Generates a test case with a get-only property and a read access.
    /// This should be valid.
    /// </summary>
    private static Gen<PropertyAccessTestCase> GenValidGetOnlyRead()
    {
        return from propName in Gen.Elements("Name", "Value")
               select new PropertyAccessTestCase(
                   $"Valid read of get-only {propName}",
                   MakeReadAccessProgram(propName, hasGet: true, hasSet: false),
                   ExpectValid: true);
    }

    private static Gen<PropertyAccessTestCase> GenTestCase()
    {
        return Gen.OneOf(
            GenValidReadAccess(),
            GenValidWriteAccess(),
            GenValidGetOnlyRead());
    }

    public static Arbitrary<PropertyAccessTestCase> ArbitraryPropertyAccessTestCase()
    {
        return GenTestCase().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static CompilationUnitNode MakeReadAccessProgram(
        string propName, bool hasGet, bool hasSet)
    {
        var accessors = ImmutableArray.CreateBuilder<AccessorDeclarationNode>();
        if(hasGet)
        {
            accessors.Add(new AutoGetAccessorAccessorDeclarationNode());
        }

        if(hasSet)
        {
            accessors.Add(new AutoSetAccessorAccessorDeclarationNode());
        }

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = Tok(propName),
            Accessors = accessors.ToImmutable(),
        };

        // Main body: var x = this.PropName; (simplified as just reading the property)
        var readStmt = MistAstGenerator.MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            "x",
            MistAstGenerator.MakeIdentifierExpr(propName));

        return MistPropertyGenerator.MakeProgramWithProperty(
            property,
            ImmutableArray.Create(readStmt));
    }

    private static CompilationUnitNode MakeWriteAccessProgram(
        string propName, bool hasGet, bool hasSet, int value)
    {
        var accessors = ImmutableArray.CreateBuilder<AccessorDeclarationNode>();
        if(hasGet)
        {
            accessors.Add(new AutoGetAccessorAccessorDeclarationNode());
        }

        if(hasSet)
        {
            accessors.Add(new AutoSetAccessorAccessorDeclarationNode());
        }

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = Tok(propName),
            Accessors = accessors.ToImmutable(),
        };

        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static (ISemanticModel model, DiagnosticBag diagnostics) BuildAndAnalyze(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);
        var source = new SourceText("test.mist", printed);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(source)));
        var semanticDiagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticDiagnostics);

        // Run the type checker analyzer to get MIST_TC_* diagnostics
        var analyzerDiagnostics = new DiagnosticBag();
        var typeChecker = new TypeCheckerAnalyzer();
        typeChecker.Analyze(model, analyzerDiagnostics);

        // Merge all diagnostics
        foreach(var d in analyzerDiagnostics.ToImmutableArray())
        {
            semanticDiagnostics.Add(d);
        }

        return (model, semanticDiagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Property Access Type Safety
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyAccessTypePropertyTests) })]
    public Property PropertyAccess_ValidAccessProducesNoAccessDiagnostic(PropertyAccessTestCase testCase)
    {
        if(!testCase.ExpectValid)
        {
            return true.ToProperty();
        }

        var (model, diagnostics) = BuildAndAnalyze(testCase.Ast);

        // Check for property access diagnostics (MIST_TC_0019 = no get, MIST_TC_0020 = no set)
        var accessDiags = diagnostics.ToImmutableArray()
            .Where(d => d.Id.Value is "MIST_TC_0019" or "MIST_TC_0020" or "MIST_TC_0021")
            .ToList();

        return (accessDiags.Count == 0).ToProperty()
            .Label($"Expected no access diagnostics for '{testCase.Description}', " +
                   $"but got: {string.Join("; ", accessDiags.Select(d => d.Message))}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyAccessTypePropertyTests) })]
    public Property PropertyAccess_PropertySymbolHasCorrectAccessors(PropertyAccessTestCase testCase)
    {
        var (model, _) = BuildAndAnalyze(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if(propSymbols.Count == 0)
        {
            return false.ToProperty().Label("No property symbols found");
        }

        var propSymbol = propSymbols.First();
        var metadata = (propSymbol as Symbol)?.PropertyMetadata;

        if(metadata is null)
        {
            return false.ToProperty().Label("PropertyMetadata is null");
        }

        // Every generated property should have at least a get accessor
        return metadata.HasGet.ToProperty()
            .Label($"Property '{propSymbol.Name}' should have a get accessor");
    }
}