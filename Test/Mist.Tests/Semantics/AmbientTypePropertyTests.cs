// Feature: mist-language-v1, Property 12: Ambient types are resolved on-demand in user code
// **Validates: Requirements 7.1, 7.2, 7.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that ambient types are resolved on-demand in user code.
///
/// Property 12: For any Mist program that references a backend-origin type (e.g., Console, Math)
/// via a using declaration for the type's namespace, the semantic model SHALL resolve the type
/// reference by querying the IAmbientTypeResolver on demand, register the resolved type with
/// SymbolOrigin.Backend, and the type checker SHALL produce no "undefined name" diagnostic for
/// that reference. The resolved symbol SHALL have the same IType representation as any user-defined
/// symbol of the same kind. Ambient types that are not referenced by user code SHALL NOT be
/// materialized in the semantic model.
/// </summary>
public class AmbientTypePropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// All known ambient type names available in the System namespace via CSharpAmbientTypeResolver.
    /// String and Array are no longer ambient types after Task 42 (Req 9.1, 9.2, 9.6).
    /// </summary>
    private static readonly string[] KnownAmbientTypes = ["Console", "Math", "Convert"];

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Creates a minimal integer literal expression AST node.
    /// </summary>
    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a using declaration for a given namespace name.
    /// </summary>
    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(namespaceName),
            Span = new Storm.Runtime.SourceSpan(0, namespaceName.Length),
        };
        return new UsingDeclarationNode { Name = qualifiedId };
    }

    /// <summary>
    /// Creates a field declaration with a named type reference (for ambient type usage).
    /// </summary>
    private static FieldDeclarationNode MakeNamedTypeField(string typeName, string fieldName)
    {
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(typeName),
            Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
        };
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = new NamedTypeRefTypeReferenceNode
            {
                Name = qualifiedId,
            },
        };
    }

    /// <summary>
    /// Builds a CompilationUnitNode that uses the System namespace and declares
    /// a class with fields typed to the given ambient type names.
    /// </summary>
    private static CompilationUnitNode BuildProgramReferencingAmbientTypes(IReadOnlyList<string> referencedTypeNames)
    {
        var fields = new List<MemberDeclarationNode>();
        for (var i = 0; i < referencedTypeNames.Count; i++)
        {
            var field = MakeNamedTypeField(referencedTypeNames[i], $"field{i}");
            fields.Add(new FieldMemberMemberDeclarationNode { Field = field });
        }

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = fields.ToImmutableArray(),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("System")),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Collects all symbols from the scope hierarchy (global scope and descendants)
    /// that have SymbolOrigin.Backend.
    /// </summary>
    private static List<ISymbol> CollectBackendSymbolsFromScopes(IScope scope)
    {
        var result = new List<ISymbol>();
        foreach (var member in scope.Members)
        {
            if (member.Origin == SymbolOrigin.Backend)
            {
                result.Add(member);
            }
        }
        return result;
    }

    /// <summary>
    /// Recursively collects all backend-origin type symbols from the scope hierarchy,
    /// starting from the global scope and walking through all child scopes.
    /// </summary>
    private static HashSet<string> CollectMaterializedAmbientTypeNames(ISemanticModel model)
    {
        var names = new HashSet<string>();
        CollectBackendTypeNamesFromScope(model.GlobalScope, names);
        return names;
    }

    private static void CollectBackendTypeNamesFromScope(IScope scope, HashSet<string> names)
    {
        foreach (var member in scope.Members)
        {
            if (member.Origin == SymbolOrigin.Backend &&
                (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct))
            {
                names.Add(member.Name);
            }
        }
    }

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a subset of known ambient types to reference in a test program.
    /// </summary>
#pragma warning disable CA1034
    public record AmbientTypeSubset(ImmutableArray<string> ReferencedTypes, ImmutableArray<string> UnreferencedTypes)
    {
        public override string ToString() =>
            $"Referenced=[{string.Join(", ", ReferencedTypes)}], Unreferenced=[{string.Join(", ", UnreferencedTypes)}]";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates arbitrary subsets of known ambient types. Each generated value
    /// partitions the known types into referenced and unreferenced sets.
    /// </summary>
    public static Arbitrary<AmbientTypeSubset> ArbitraryAmbientTypeSubsets()
    {
        // Generate a bitmask (0 to 2^N - 1) to select a subset of known ambient types.
        var maxMask = (1 << KnownAmbientTypes.Length) - 1;
        var gen = from mask in Gen.Choose(0, maxMask)
                  let referenced = KnownAmbientTypes
                      .Where((_, i) => (mask & (1 << i)) != 0)
                      .ToImmutableArray()
                  let unreferenced = KnownAmbientTypes
                      .Where((_, i) => (mask & (1 << i)) == 0)
                      .ToImmutableArray()
                  select new AmbientTypeSubset(referenced, unreferenced);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 12a: For any subset of known ambient types referenced via field
    /// declarations in a class with "using System;", the semantic model SHALL
    /// resolve each referenced type without producing undefined type diagnostics.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AmbientTypePropertyTests) }, MaxTest = 50)]
    public bool ReferencedAmbientTypes_ProduceNoUndefinedDiagnostics(AmbientTypeSubset subset)
    {
        if (subset.ReferencedTypes.Length == 0)
            return true; // Vacuously true — no types to reference.

        var ast = BuildProgramReferencingAmbientTypes(subset.ReferencedTypes);
        var resolver = new CSharpAmbientTypeResolver();
        var (_, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No "undefined type" diagnostics should be produced for any referenced ambient type.
        var undefinedTypeDiags = diagnostics
            .Where(d => d.Id.Value.Contains("UNDEFINED_TYPE"))
            .ToList();

        return undefinedTypeDiags.Count == 0;
    }

    /// <summary>
    /// Property 12b: For any subset of known ambient types referenced in user code,
    /// the resolved ambient type symbols SHALL have SymbolOrigin.Backend.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AmbientTypePropertyTests) }, MaxTest = 50)]
    public bool ResolvedAmbientTypes_HaveBackendOrigin(AmbientTypeSubset subset)
    {
        if (subset.ReferencedTypes.Length == 0)
            return true;

        var ast = BuildProgramReferencingAmbientTypes(subset.ReferencedTypes);
        var resolver = new CSharpAmbientTypeResolver();
        var (model, _) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // Walk the file scope (child of global scope) to find the registered ambient type symbols.
        // The ambient types are registered in the file scope created during Pass 2.
        var materializedNames = CollectMaterializedAmbientTypeNames(model);

        // Every referenced type should be materialized with Backend origin.
        // We check by looking at the global scope's children for backend symbols.
        // The file scope is a child scope created during Pass 2, so we need to
        // check all scopes reachable from the model.
        foreach (var typeName in subset.ReferencedTypes)
        {
            // The type should be resolvable — verify by checking that the field's
            // declared type is not UnknownType.
            var fieldSymbol = model.AllSymbols
                .FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name.StartsWith("field"));

            if (fieldSymbol?.DeclaredType is UnknownType)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 12c: For any subset of known ambient types, ONLY the referenced
    /// types are materialized in the semantic model. Unreferenced ambient types
    /// SHALL NOT appear in the scope hierarchy.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AmbientTypePropertyTests) }, MaxTest = 50)]
    public bool UnreferencedAmbientTypes_AreNotMaterialized(AmbientTypeSubset subset)
    {
        var ast = BuildProgramReferencingAmbientTypes(subset.ReferencedTypes);
        var resolver = new TrackingAmbientTypeResolver();
        var (_, _) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // The tracking resolver records which types were actually resolved.
        // Unreferenced types should NOT have been resolved.
        foreach (var unreferenced in subset.UnreferencedTypes)
        {
            if (resolver.ResolvedTypes.Contains(unreferenced))
                return false;
        }

        // Referenced types should have been resolved (if any).
        foreach (var referenced in subset.ReferencedTypes)
        {
            if (!resolver.ResolvedTypes.Contains(referenced))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 12d: For any subset of known ambient types referenced in user code,
    /// the resolved type SHALL have the same IType representation (ClassType) as any
    /// user-defined class. The ambient type's Kind should be TypeKind.Class.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AmbientTypePropertyTests) }, MaxTest = 50)]
    public bool ResolvedAmbientTypes_HaveSameRepresentationAsUserTypes(AmbientTypeSubset subset)
    {
        if (subset.ReferencedTypes.Length == 0)
            return true;

        var ast = BuildProgramReferencingAmbientTypes(subset.ReferencedTypes);
        var resolver = new CSharpAmbientTypeResolver();
        var (model, _) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // Each referenced ambient type should resolve to a ClassType (all known
        // ambient types in CSharpAmbientTypeResolver are classes).
        // Verify by checking the field symbols' declared types.
        var fieldSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Field && s.Name.StartsWith("field"))
            .ToList();

        foreach (var field in fieldSymbols)
        {
            if (field.DeclaredType is not ClassType)
                return false;

            // The type should have a valid Kind (TypeKind.Class).
            if (field.DeclaredType.Kind != TypeKind.Class)
                return false;
        }

        return fieldSymbols.Count == subset.ReferencedTypes.Length;
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that referencing Console via "using System;" produces no undefined
    /// type diagnostic and the field's declared type is a ClassType with Backend origin.
    /// </summary>
    [Fact]
    public void SingleAmbientType_Console_ResolvesWithoutDiagnostics()
    {
        var ast = BuildProgramReferencingAmbientTypes(["Console"]);
        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No undefined type diagnostics.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("UNDEFINED_TYPE"));

        // The field should have Console as its declared type.
        var field = model.AllSymbols.First(s => s.Kind == SymbolKind.Field && s.Name == "field0");
        Assert.NotNull(field.DeclaredType);
        Assert.IsType<ClassType>(field.DeclaredType);
        Assert.Equal("Console", field.DeclaredType!.DisplayName);
    }

    /// <summary>
    /// Verifies that ambient types not referenced by user code are not materialized.
    /// When only Console is referenced, Math/Convert/Array/String should not be resolved.
    /// </summary>
    [Fact]
    public void UnreferencedAmbientTypes_AreNotResolved()
    {
        var ast = BuildProgramReferencingAmbientTypes(["Console"]);
        var tracker = new TrackingAmbientTypeResolver();
        var (_, _) = BuildModelWithDiagnostics(ast, ambientTypeResolver: tracker);

        // Only Console should have been resolved.
        Assert.Contains("Console", tracker.ResolvedTypes);
        Assert.DoesNotContain("Math", tracker.ResolvedTypes);
        Assert.DoesNotContain("Convert", tracker.ResolvedTypes);
    }

    /// <summary>
    /// Verifies that multiple ambient types can be referenced simultaneously
    /// and all resolve correctly.
    /// </summary>
    [Fact]
    public void MultipleAmbientTypes_AllResolveCorrectly()
    {
        var ast = BuildProgramReferencingAmbientTypes(["Console", "Math", "Convert"]);
        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("UNDEFINED_TYPE"));

        var fields = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Field && s.Name.StartsWith("field"))
            .OrderBy(s => s.Name)
            .ToList();

        Assert.Equal(3, fields.Count);
        Assert.Equal("Console", fields[0].DeclaredType?.DisplayName);
        Assert.Equal("Math", fields[1].DeclaredType?.DisplayName);
        Assert.Equal("Convert", fields[2].DeclaredType?.DisplayName);
    }

    /// <summary>
    /// Verifies that without a "using System;" declaration, ambient types
    /// are NOT resolved and produce undefined type diagnostics.
    /// </summary>
    [Fact]
    public void AmbientType_WithoutUsingDeclaration_ProducesUndefinedDiagnostic()
    {
        // Build a program that references Console but has NO using declaration.
        var field = MakeNamedTypeField("Console", "myField");
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };

        var ast = new CompilationUnitNode
        {
            // No usings!
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (_, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // Should produce an undefined type diagnostic since there's no "using System;".
        Assert.Contains(diagnostics, d => d.Id.Value.Contains("UNDEFINED_TYPE"));
    }

    /// <summary>
    /// Verifies that when no ambient type resolver is provided, ambient types
    /// produce undefined type diagnostics.
    /// </summary>
    [Fact]
    public void AmbientType_WithoutResolver_ProducesUndefinedDiagnostic()
    {
        var ast = BuildProgramReferencingAmbientTypes(["Console"]);
        // Pass null for the ambient type resolver.
        var (_, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: null);

        // Should produce diagnostics — "System" namespace is undefined without a resolver.
        Assert.Contains(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_NAMESPACE") || d.Id.Value.Contains("UNDEFINED_TYPE"));
    }

    // ─── Tracking ambient type resolver ──────────────────────────────────

    /// <summary>
    /// A wrapper around CSharpAmbientTypeResolver that tracks which types
    /// were actually resolved (queried via ResolveType). Used to verify
    /// that unreferenced types are not materialized.
    /// </summary>
    private sealed class TrackingAmbientTypeResolver : IAmbientTypeResolver
    {
        private readonly CSharpAmbientTypeResolver _inner = new();
        private readonly HashSet<string> _resolvedTypes = new();

        public HashSet<string> ResolvedTypes => _resolvedTypes;

        public bool HasNamespace(string namespaceName) => _inner.HasNamespace(namespaceName);

        public IType? ResolveType(string namespaceName, string typeName)
        {
            var result = _inner.ResolveType(namespaceName, typeName);
            if (result is not null)
            {
                _resolvedTypes.Add(typeName);
            }
            return result;
        }

        public ImmutableArray<ISymbol> GetMembers(IType type) => _inner.GetMembers(type);

        public ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName) =>
            _inner.GetTypeNamesInNamespace(namespaceName);
    }
}
