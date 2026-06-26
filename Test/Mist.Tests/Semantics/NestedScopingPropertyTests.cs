// Feature: nested-scoping, Property 1: Parser nesting universality
// Feature: nested-scoping, Property 3: Modifier preservation on nested types
// Feature: nested-scoping, Property 5: Namespace FQN concatenation
// Feature: nested-scoping, Property 6: Type FQN construction
// Feature: nested-scoping, Property 7: Closed generic FQN for nested types
// Feature: nested-scoping, Property 10: Same-name namespace scope sharing
// Feature: nested-scoping, Property 11: Different-name namespace scope isolation
// Feature: nested-scoping, Property 9: Type parameter shadowing
// Feature: nested-scoping, Property 12: C# emission preserves nesting structure
// Feature: nested-scoping, Property 4: Parse–print–parse round trip

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for nested scoping.
/// Uses a shared CompilerDriver instance for in-process compilation.
/// </summary>
public sealed class NestedScopingPropertyTests
{
    // ─── Shared driver (reused across all tests, available for semantic-level properties) ──

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    /// <summary>
    /// Returns the Mist keyword for any of the five type kinds.
    /// </summary>
    private static string AllTypeKindKeyword(AllTypeKind kind)
    {
        return kind switch
        {
            AllTypeKind.Class => "class",
            AllTypeKind.Struct => "struct",
            AllTypeKind.Abstract => "abstract",
            AllTypeKind.Enum => "enum",
            _ => "class"
        };
    }

    /// <summary>
    /// Builds a Mist source string with a container type containing a nested type.
    /// </summary>
    private static string BuildNestedSource(AllTypeKind containerKind, AllTypeKind nestedKind)
    {
        var containerKeyword = AllTypeKindKeyword(containerKind);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);

        var nestedBody = nestedKind == AllTypeKind.Enum
            ? "{ A, B }"
            : "{ }";

        return $"{containerKeyword} Container {{\n    {nestedKeyword} Nested {nestedBody}\n}}";
    }

    /// <summary>
    /// Extracts the MemberDeclaration members from a type declaration node.
    /// Only class and struct use MemberDeclaration (which includes NestedTypeMember).
    /// </summary>
    private static ImmutableArray<MemberDeclarationNode>? GetMembers(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            ClassTypeTypeDeclarationNode classType => classType.ClassDeclaration.Members,
            StructTypeTypeDeclarationNode structType => structType.StructDeclaration.Members,
            _ => null,
        };
    }

    /// <summary>
    /// Checks if a parsed compilation unit contains a NestedTypeMemberMemberDeclarationNode
    /// as a member of the first type declaration.
    /// </summary>
    private static bool HasNestedTypeMember(CompilationUnitNode cu)
    {
        if(cu.Items.Length == 0)
        {
            return false;
        }

        var topLevelItem = cu.Items[0];
        var typeDecl = topLevelItem switch
        {
            TypeItemTopLevelItemNode typeItem => typeItem.TypeDeclaration,
            _ => null,
        };

        if(typeDecl is null)
        {
            return false;
        }

        var members = GetMembers(typeDecl);
        if(members is null)
        {
            return false;
        }

        return members.Value.Any(m => m is NestedTypeMemberMemberDeclarationNode);
    }

    /// <summary>
    /// Gets the modifiers from a TypeDeclarationNode (any of the five type kinds).
    /// </summary>
    private static ImmutableArray<ModifierNode> GetModifiersFromTypeDeclaration(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Modifiers,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.Modifiers,
            AbstractTypeTypeDeclarationNode it => it.AbstractDeclaration.Modifiers,
            EnumTypeTypeDeclarationNode et => et.EnumDeclaration.Modifiers,
            _ => ImmutableArray<ModifierNode>.Empty
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Parser nesting universality
    //
    // For any container type kind × nested type kind, verify the parser
    // produces a NestedTypeMember node.
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: Parser nesting universality.
    /// For any container type kind (class, struct) and for any nested type kind
    /// (class, struct, abstract type, enum), the parser SHALL produce a valid
    /// AST where the nested type appears as a NestedTypeMember within the
    /// container's member list.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ParserNestingUniversality_ProducesNestedTypeMemberNode(
        NestedTypeSource input)
    {
        var result = ParseSource(input.Source);

        // Parser should return a non-null result with a CompilationUnitNode root
        if(result.Root is not CompilationUnitNode cu)
        {
            return false;
        }

        // No parse errors should be reported
        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(parseErrors.Count > 0)
        {
            return false;
        }

        // The AST should contain a NestedTypeMemberMemberDeclarationNode
        return HasNestedTypeMember(cu);
    }

    /// <summary>
    /// Deterministic verification that all 10 combinations of container (class, struct)
    /// × nested type kinds produce a NestedTypeMember node.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    /// </summary>
    [Fact]
    public void ParserNestingUniversality_All10Combinations_ProduceNestedTypeMemberNode()
    {
        var allKinds = new[]
        {
            AllTypeKind.Class,
            AllTypeKind.Struct,
            AllTypeKind.Abstract,
            AllTypeKind.Enum,
        };

        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        var failures = new List<string>();

        foreach(var containerKind in containerKinds)
        {
            foreach(var nestedKind in allKinds)
            {
                var source = BuildNestedSource(containerKind, nestedKind);
                var result = ParseSource(source);

                if(result.Root is not CompilationUnitNode cu)
                {
                    failures.Add($"{containerKind} containing {nestedKind}: parse returned null root");
                    continue;
                }

                var parseErrors = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if(parseErrors.Count > 0)
                {
                    failures.Add($"{containerKind} containing {nestedKind}: parse errors: {string.Join("; ", parseErrors.Select(e => e.Message))}");
                    continue;
                }

                if(!HasNestedTypeMember(cu))
                {
                    failures.Add($"{containerKind} containing {nestedKind}: no NestedTypeMemberMemberDeclarationNode found");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"Failed combinations ({failures.Count}/10):\n" + string.Join("\n", failures));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Nesting depth acceptance
    //
    // For any depth 1–8 and any sequence of type kinds, verify the parser
    // produces an AST with exactly that depth of nesting.
    //
    // **Validates: Requirements 1.6, 2.3, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2: Nesting depth acceptance.
    /// For any depth 1–8 and any sequence of type kinds, verify the parser produces
    /// an AST with exactly that depth of nesting.
    /// **Validates: Requirements 1.6, 2.3, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool NestingDepthAcceptance_ParserProducesCorrectDepth(NestedTypeDepthSource input)
    {
        var result = ParseSource(input.Source);

        // Parser should produce a valid AST without errors
        if(result.Root is not CompilationUnitNode cu)
        {
            return false;
        }

        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(parseErrors.Count > 0)
        {
            return false;
        }

        // Verify the AST has exactly the expected nesting depth
        var measuredDepth = MeasureNestingDepth(cu);
        return measuredDepth == input.Depth;
    }

    /// <summary>
    /// Measures the maximum nesting depth of type declarations in a CompilationUnit.
    /// A single top-level type has depth 1. A type nested inside another has depth 2, etc.
    /// </summary>
    private static int MeasureNestingDepth(CompilationUnitNode cu)
    {
        var maxDepth = 0;

        foreach(var item in cu.Items)
        {
            if(item is TypeItemTopLevelItemNode typeItem)
            {
                var depth = MeasureTypeDepth(typeItem.TypeDeclaration, 1);
                if(depth > maxDepth)
                {
                    maxDepth = depth;
                }
            }
        }

        return maxDepth;
    }

    /// <summary>
    /// Recursively measures the nesting depth of a TypeDeclarationNode.
    /// </summary>
    private static int MeasureTypeDepth(TypeDeclarationNode typeDecl, int currentDepth)
    {
        var members = GetMembers(typeDecl);
        if(members is null || members.Value.IsEmpty)
        {
            return currentDepth;
        }

        var maxChildDepth = currentDepth;

        foreach(var member in members.Value)
        {
            if(member is NestedTypeMemberMemberDeclarationNode nestedType)
            {
                var childDepth = MeasureTypeDepth(nestedType.NestedType, currentDepth + 1);
                if(childDepth > maxChildDepth)
                {
                    maxChildDepth = childDepth;
                }
            }
        }

        return maxChildDepth;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Modifier preservation on nested types
    //
    // For any subset of {public, private, static} applied to a nested type
    // declaration, the parser SHALL produce an AST node whose modifier list
    // contains exactly those modifiers.
    //
    // **Validates: Requirements 1.7**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 3: Modifier preservation on nested types.
    /// For any subset of {public, private, static} and any nested type kind,
    /// the parser SHALL produce an AST node whose modifier list contains exactly
    /// those modifiers when the nested type is inside a class container.
    ///
    /// **Validates: Requirements 1.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ModifierPreservation_NestedType_InClass(
        ModifierSubset mods, AllTypeKind nestedKind)
    {
        var modSource = MistNestedScopingGenerator.ModifierSetToSource(mods);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);
        var nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";
        var source = $"class Outer {{ {modSource}{nestedKeyword} Inner {nestedBody} }}";
        return VerifyNestedTypeModifiers(source, mods);
    }

    /// <summary>
    /// Property 3: Modifier preservation on nested types.
    /// Same as above but with a struct container.
    ///
    /// **Validates: Requirements 1.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ModifierPreservation_NestedType_InStruct(
        ModifierSubset mods, AllTypeKind nestedKind)
    {
        var modSource = MistNestedScopingGenerator.ModifierSetToSource(mods);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);
        var nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";
        var source = $"struct Outer {{ {modSource}{nestedKeyword} Inner {nestedBody} }}";
        return VerifyNestedTypeModifiers(source, mods);
    }

    // ─── Property 3 verification helper ──────────────────────────────────

    private static bool VerifyNestedTypeModifiers(string source, ModifierSubset expectedMods)
    {
        var result = ParseSource(source);

        // Parse must succeed without errors
        if(result.Root is not CompilationUnitNode cu)
        {
            return false;
        }

        if(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        var types = cu.GetTypes();
        if(types.Length == 0)
        {
            return false;
        }

        // Get the outer type's members
        var members = GetMembers(types[0]);
        if(members is null || members.Value.Length == 0)
        {
            return false;
        }

        // Find the nested type member
        var nestedMember = members.Value.OfType<NestedTypeMemberMemberDeclarationNode>().FirstOrDefault();
        if(nestedMember is null)
        {
            return false;
        }

        // Get the modifiers from the nested type declaration
        var actualModifiers = GetModifiersFromTypeDeclaration(nestedMember.NestedType);

        // Verify count matches
        var expectedCount = MistNestedScopingGenerator.ModifierCount(expectedMods);
        if(actualModifiers.Length != expectedCount)
        {
            return false;
        }

        // Verify each expected modifier is present by type
        var expectedTypes = MistNestedScopingGenerator.ExpectedModifierNodeTypes(expectedMods);
        var actualTypes = actualModifiers.Select(m => m.GetType()).ToHashSet();

        return expectedTypes.SetEquals(actualTypes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Type FQN construction
    //
    // For any type declared inside a namespace (single-segment, multi-segment,
    // or file-scoped), the Semantic Model Builder SHALL compute the type's FQN
    // as the namespace path followed by a dot and the type's DisplayName.
    //
    // **Validates: Requirements 6.1, 6.2, 6.3, 8.1, 8.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: Type FQN construction.
    /// For any type declared inside a namespace (single-segment, multi-segment,
    /// or file-scoped), verify that the type's ContainingNamespace equals the
    /// namespace path and that the FQN equals namespace + "." + DisplayName.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 8.1, 8.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool TypeFqnConstruction_EqualsNamespaceDotDisplayName(
        TypeInNamespaceSource input)
    {
        return VerifyTypeFqn(input);
    }

    // ─── Property 6 verification helper ──────────────────────────────────

    private static bool VerifyTypeFqn(TypeInNamespaceSource input)
    {
        // Build the semantic model directly to inspect type registration
        var sourceText = new SourceText("test.mist", input.Source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var parseResult = Parser.Parse(tokens, bag);

        if(parseResult.Root is null)
        {
            return false;
        }

        // Check for parse errors
        if(bag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var builder = new SemanticModelBuilder();
        var semanticBag = new DiagnosticBag();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semanticBag);

        // Check for semantic errors
        if(semanticBag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        // Find the type symbol by name (user-defined types only)
        var typeSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == input.TypeName &&
            s.Origin == SymbolOrigin.UserCode &&
            s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract
                or SymbolKind.Enum);

        if(typeSymbol is null)
        {
            return false;
        }

        // Extract ContainingNamespace from the declared type
        var containingNamespace = GetContainingNamespace(typeSymbol.DeclaredType);
        if(containingNamespace is null)
        {
            return false;
        }

        // Verify ContainingNamespace matches the expected namespace path
        if(containingNamespace != input.NamespacePath)
        {
            return false;
        }

        // Verify FQN = namespace + "." + DisplayName
        var displayName = typeSymbol.DeclaredType!.DisplayName;
        var expectedFqn = containingNamespace + "." + displayName;

        return expectedFqn == input.ExpectedFqn;
    }

    /// <summary>
    /// Extracts the ContainingNamespace from a type (ClassType, StructType, etc.).
    /// </summary>
    private static string? GetContainingNamespace(IType? type)
    {
        return type switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            AbstractType it => it.ContainingNamespace,
            EnumType et => et.ContainingNamespace,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Namespace FQN concatenation
    //
    // For any chain of nested block namespaces with names [n₁, n₂, ..., nₖ],
    // the Semantic Model Builder SHALL compute the FQN of the innermost
    // namespace as n₁.n₂...nₖ. When a file-scoped namespace f precedes the
    // chain, the FQN SHALL be f.n₁.n₂...nₖ.
    //
    // **Validates: Requirements 5.2, 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps the real SemanticModelBuilder to capture the constructed model
    /// so tests can inspect namespace symbols registered in the GlobalScope.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    /// <summary>
    /// Compiles source using a CapturingSemanticModelBuilder and returns both
    /// the result and the captured semantic model for inspection.
    /// </summary>
    private static (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var capture = new CapturingSemanticModelBuilder();
            var driver = new CompilerDriver(
                new Mist.Lexing.MistLexer(),
                new Mist.Parsing.MistParser(),
                capture,
                ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
                ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = driver.Compile(options);
            return (result, capture.Captured);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Property 5: Namespace FQN concatenation.
    /// For any chain of nested block namespaces with names [n₁, n₂, ..., nₖ],
    /// the Semantic Model Builder SHALL compute the FQN of each namespace level
    /// as the dot-joined names up to that level. When a file-scoped namespace f
    /// precedes the chain, the FQN SHALL be f.n₁.n₂...nₖ.
    ///
    /// **Validates: Requirements 5.2, 5.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool NamespaceFqnConcatenation_NestedBlockNamespaces_ProduceCorrectFqns(
        NamespaceChainSource input)
    {
        var (result, model) = CompileAndCapture(input.Source);

        // Compilation should succeed (no semantic errors for valid namespace nesting)
        if(model is null)
        {
            return false;
        }

        // Check that all expected namespace FQNs are registered as symbols in the GlobalScope
        var namespaceSymbols = model.GlobalScope.Members
            .Where(s => s.Kind == SymbolKind.Namespace)
            .Select(s => s.Name)
            .ToHashSet();

        foreach(var expectedFqn in input.ExpectedFqns)
        {
            if(!namespaceSymbols.Contains(expectedFqn))
            {
                return false;
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Closed generic FQN for nested types
    //
    // For any nested type inside one or more generic ancestor types, the FQN
    // SHALL include the closed generic form (concrete type arguments) at each
    // ancestor level. If the nested type itself is generic, its own type
    // arguments SHALL also appear in the FQN.
    //
    // NOTE: The closed-generic FQN computation (ComputeNestedTypeFqn) is not
    // yet implemented in the semantic model. This test verifies the
    // prerequisites: that nested types inside generic parents are correctly
    // registered in the parent's scope and that parent types have the expected
    // type parameters. Once ComputeNestedTypeFqn is implemented, the FQN
    // assertion can be enabled.
    //
    // **Validates: Requirements 8.2, 8.3, 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7: Closed generic FQN for nested types.
    /// For any nested type inside generic ancestors, verifies that:
    /// 1. The source compiles without errors (nested type is correctly registered)
    /// 2. Each parent type has the expected number of type parameters
    /// 3. The nested type is registered in the correct scope (parent type's member scope)
    /// 4. If the nested type is itself generic, it has the expected type parameters
    ///
    /// TODO: Once ComputeNestedTypeFqn is implemented, add assertion that the FQN
    /// uses closed generic forms (e.g., "Outer&lt;int&gt;.Inner" not "Outer&lt;T&gt;.Inner").
    ///
    /// **Validates: Requirements 8.2, 8.3, 8.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ClosedGenericFqn_NestedTypeInGenericParent_RegisteredCorrectly(
        GenericNestedFqnSource input)
    {
        // Build the semantic model directly to inspect type registration.
        // We use BuildSemanticModel (in-process, no spawning) rather than CompileSource
        // because the generated source doesn't include an entry point (Main method)
        // and the full pipeline's type checker would reject it.
        var model = BuildSemanticModel(input.Source);
        if(model is null)
        {
            return false;
        }

        // Step 1: Verify each parent type has the expected number of type parameters
        for(var i = 0; i < input.ParentTypeNames.Count; i++)
        {
            var parentName = input.ParentTypeNames[i];
            var expectedParamCount = input.ParentTypeParamCounts[i];

            var parentSymbol = model.AllSymbols.FirstOrDefault(s =>
                s.Name == parentName &&
                s.Kind == SymbolKind.Class &&
                s.Origin == SymbolOrigin.UserCode);

            if(parentSymbol is null)
            {
                return false;
            }

            var parentType = parentSymbol.DeclaredType;
            var actualParamCount = GetTypeParameterCount(parentType);

            if(actualParamCount != expectedParamCount)
            {
                return false;
            }
        }

        // Step 2: Verify the nested type exists and is registered
        var nestedSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == input.NestedTypeName &&
            s.Kind == SymbolKind.Class &&
            s.Origin == SymbolOrigin.UserCode);

        if(nestedSymbol is null)
        {
            return false;
        }

        // Step 3: If the nested type is itself generic, verify its type parameter count
        if(input.NestedTypeParamCount > 0)
        {
            var nestedType = nestedSymbol.DeclaredType;
            var actualNestedParamCount = GetTypeParameterCount(nestedType);

            if(actualNestedParamCount != input.NestedTypeParamCount)
            {
                return false;
            }
        }

        // Step 4: Verify the nested type's containing scope is the innermost parent's member scope
        var innermostParentName = input.ParentTypeNames[input.ParentTypeNames.Count - 1];
        var innermostParentSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == innermostParentName &&
            s.Kind == SymbolKind.Class &&
            s.Origin == SymbolOrigin.UserCode);

        if(innermostParentSymbol?.DeclaredType is not ClassType innermostParentClassType)
        {
            return false;
        }

        // The nested type should be registered in the innermost parent's member scope
        var nestedInParentScope = innermostParentClassType.MemberScope.Members.Any(m =>
            m.Name == input.NestedTypeName && m.Kind == SymbolKind.Class);

        if(!nestedInParentScope)
        {
            return false;
        }

        // TODO: Once ComputeNestedTypeFqn is implemented, verify the FQN uses closed
        // generic forms. For example:
        //   - Single generic parent: "Outer<int>.Inner" (not "Outer<T>.Inner")
        //   - Multiple levels: "Level0<string>.Level1<int>.Inner"
        //   - Nested also generic: "Outer<int>.Inner<string>"

        return true;
    }

    // ─── Property 7 helpers ──────────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model from source text for symbol table inspection.
    /// Uses the lexer/parser and SemanticModelBuilder directly (in-process, no spawning).
    /// Returns null if the model cannot be built.
    /// </summary>
    private static ISemanticModel? BuildSemanticModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var parseResult = Parser.Parse(tokens, bag);

        if(parseResult.Root is null)
        {
            return null;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var builder = new SemanticModelBuilder();
        var semanticBag = new DiagnosticBag();

        return builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semanticBag);
    }

    /// <summary>
    /// Gets the number of type parameters for a given type.
    /// </summary>
    private static int GetTypeParameterCount(IType? type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters.Count,
            StructType st => st.TypeParameters.Count,
            AbstractType it => it.TypeParameters.Count,
            _ => 0,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Ancestor type parameter visibility
    //
    // For any nested type at depth d inside generic ancestor types, all
    // type parameters from all d ancestor levels SHALL be resolvable by
    // name within the nested type's scope.
    //
    // **Validates: Requirements 7.1, 7.2, 7.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 8: Ancestor type parameter visibility.
    /// For any nested type at depth d (1–4) inside generic ancestor types,
    /// all ancestor type parameters SHALL be resolvable within the nested type's scope.
    /// The test generates generic parent types at each level and verifies that using
    /// their type parameters as field types in the innermost nested type produces
    /// no type resolution errors.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool AncestorTypeParameterVisibility_AllAncestorParamsResolvable(
        AncestorTypeParamSource input)
    {
        var result = CompileSource(input.Source);

        // Filter out the entry point diagnostic — we're testing type parameter resolution,
        // not program structure. MIST_TC_0017 = "Program does not contain a valid entry point"
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        return errors.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Type parameter shadowing
    //
    // For any nested type that declares a type parameter with the same name
    // as an ancestor's type parameter, all references to that name within
    // the nested type SHALL resolve to the nested type's own parameter,
    // not the ancestor's.
    //
    // **Validates: Requirements 7.4**
    // ═══════════════════════════════════════════════════════════════════════

    // ─── Property 9 helper ───────────────────────────────────────────────

    private static CompilerResult CompileSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            return SharedDriver.Compile(options);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Property 9: Type parameter shadowing.
    /// For any nested type that re-declares a parent's type parameter name,
    /// the compiler SHALL accept the code without errors — the nested type's
    /// parameter shadows the ancestor's parameter within the nested type's scope.
    ///
    /// This verifies that Scope.Lookup() checks local members first before
    /// delegating to the parent scope, so the nested type's own type parameter
    /// takes precedence over the ancestor's.
    ///
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool TypeParameterShadowing_NestedTypeParamTakesPrecedence(
        TypeParamShadowingSource input)
    {
        var result = CompileSource(input.Source);

        // Shadowing is valid — compilation should succeed with no errors.
        // Filter out MIST_TC_0017 (entry point not found) since we're testing
        // type parameter resolution, not program structure.
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        return errors.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Same-name namespace scope sharing
    //
    // For any file containing multiple block namespace declarations with
    // the same qualified name, all type declarations from each block SHALL
    // be resolvable by simple name from within any other block of the same
    // namespace in that file.
    //
    // **Validates: Requirements 4.1**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 10: Same-name namespace scope sharing.
    /// For any file with multiple same-name block namespaces, all types from
    /// each block are registered in a single shared namespace scope and are
    /// cross-resolvable by simple name.
    ///
    /// The test generates N-1 blocks each declaring a class, and a final block
    /// declaring a class with fields referencing all types from the earlier blocks.
    /// The compiler should produce no errors, proving the types share a scope.
    ///
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool SameNameNamespaceScopeSharing_TypesCrossResolvable(
        SameNameNamespaceSource input)
    {
        var result = CompileSource(input.Source);

        // Filter out the entry-point diagnostic (MIST_TC_0017) — it's unrelated
        // to namespace scope sharing and fires because generated sources don't
        // include a Main method.
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        return errors.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Different-name namespace scope isolation
    //
    // For any file containing block namespace declarations with different
    // qualified names, types declared in one namespace SHALL NOT be
    // resolvable by simple name from within a block of a different
    // namespace (without a using declaration).
    //
    // **Validates: Requirements 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 11: Different-name namespace scope isolation.
    /// For any file with block namespace declarations with different names,
    /// a type declared in one namespace SHALL NOT be resolvable by simple name
    /// from within a block of a different namespace without a using declaration.
    ///
    /// The test generates two block namespaces with different names. The first
    /// declares a class, and the second declares a class with a field referencing
    /// the first class by simple name. The compiler should produce a
    /// MIST_UNDEFINED_TYPE diagnostic because the type is not visible across
    /// different namespace scopes.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool DifferentNameNamespaceScopeIsolation_TypesNotCrossResolvable(
        DifferentNamespacePair nsPair)
    {
        // Build source: two different-name block namespaces where the second
        // references a type from the first by simple name (no using).
        var source =
            $"namespace {nsPair.Namespace1} {{\n" +
            $"    class Target {{ }}\n" +
            $"}}\n" +
            $"\n" +
            $"namespace {nsPair.Namespace2} {{\n" +
            $"    class Consumer {{\n" +
            $"        Target myTarget;\n" +
            $"    }}\n" +
            $"}}";

        var result = CompileSource(source);

        // Should produce MIST_UNDEFINED_TYPE for "Target" since it's in a
        // different namespace and no using declaration is present.
        var hasUndefinedType = result.Diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" &&
            d.Message.Contains("Target"));

        return hasUndefinedType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: C# emission preserves nesting structure
    //
    // For any nested type declaration, the C# emitter SHALL emit the type
    // declaration inside the parent type's body, and SHALL NOT re-declare
    // the parent's type parameters on the nested type's declaration.
    //
    // **Validates: Requirements 10.1, 10.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 12: C# emission preserves nesting structure.
    /// For any nested type declaration (with or without generic parent), the C#
    /// emitter SHALL emit the nested type inside the parent type's body (between
    /// its braces) and SHALL NOT re-declare the parent's type parameters on the
    /// nested type's declaration.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool CSharpEmission_PreservesNestingStructure(CSharpNestingSource input)
    {
        var result = CompileSource(input.Source);

        // Compilation must succeed (ignoring entry point diagnostic)
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        if(errors.Count > 0)
        {
            return false;
        }

        // Get the emitted C# source
        var csharpSource = GetEmittedCSharp(result);
        if(csharpSource is null)
        {
            return false;
        }

        // Verify 1: The nested type declaration appears INSIDE the parent type's body.
        if(!NestedTypeIsInsideParentBody(csharpSource, input))
        {
            return false;
        }

        // Verify 2: The nested type does NOT re-declare the parent's type parameters.
        // If the parent has type params <T0, T1, ...>, the nested type's declaration
        // line should NOT contain those params (unless they are the nested type's own).
        if(input.ParentTypeParams.Count > 0)
        {
            if(NestedTypeRedeclaresParentTypeParams(csharpSource, input))
            {
                return false;
            }
        }

        return true;
    }

    // ─── Property 12 helpers ─────────────────────────────────────────────

    /// <summary>
    /// Extracts the emitted C# source from a CompilerResult.
    /// Looks for the file containing "namespace TestProgram".
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the C# keyword for a given Mist type kind.
    /// Mist enums emit as record classes in C#.
    /// </summary>
    private static string GetCSharpKeyword(AllTypeKind kind)
    {
        return kind switch
        {
            AllTypeKind.Class => "class",
            AllTypeKind.Struct => "struct",
            AllTypeKind.Abstract => "interface",
            AllTypeKind.Enum => "record class",
            _ => "class"
        };
    }

    /// <summary>
    /// Verifies that the nested type declaration appears inside the parent type's body
    /// (between the parent's opening and closing braces) in the emitted C# source.
    /// </summary>
    private static bool NestedTypeIsInsideParentBody(string csharpSource, CSharpNestingSource input)
    {
        var nestedCSharpKeyword = GetCSharpKeyword(input.NestedKind);

        // Find the parent type declaration line
        var parentKeyword = GetCSharpKeyword(input.ContainerKind);
        var parentDeclPattern = $"{parentKeyword} {input.ParentName}";
        var parentDeclIndex = csharpSource.IndexOf(parentDeclPattern, StringComparison.Ordinal);
        if(parentDeclIndex < 0)
        {
            return false;
        }

        // Find the parent's opening brace after the declaration
        var parentOpenBrace = csharpSource.IndexOf('{', parentDeclIndex);
        if(parentOpenBrace < 0)
        {
            return false;
        }

        // Find the matching closing brace for the parent (track brace depth)
        var parentCloseBrace = FindMatchingCloseBrace(csharpSource, parentOpenBrace);
        if(parentCloseBrace < 0)
        {
            return false;
        }

        // The nested type declaration should appear between the parent's braces
        var parentBody = csharpSource.Substring(parentOpenBrace + 1, parentCloseBrace - parentOpenBrace - 1);
        var nestedDeclPattern = $"{nestedCSharpKeyword} {input.NestedName}";

        return parentBody.Contains(nestedDeclPattern, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the nested type's declaration line does NOT contain the parent's
    /// type parameters. This checks that the emitter doesn't re-declare parent type
    /// params on the nested type.
    /// </summary>
    private static bool NestedTypeRedeclaresParentTypeParams(string csharpSource, CSharpNestingSource input)
    {
        var nestedCSharpKeyword = GetCSharpKeyword(input.NestedKind);
        var nestedDeclPattern = $"{nestedCSharpKeyword} {input.NestedName}";

        var nestedDeclIndex = csharpSource.IndexOf(nestedDeclPattern, StringComparison.Ordinal);
        if(nestedDeclIndex < 0)
        {
            return false;
        }

        // Extract the rest of the line from the nested type declaration
        var lineEnd = csharpSource.IndexOf('\n', nestedDeclIndex);
        if(lineEnd < 0)
        {
            lineEnd = csharpSource.Length;
        }

        var nestedDeclLine = csharpSource[nestedDeclIndex..lineEnd];

        // Check if the nested type's declaration line contains a type parameter list
        // that includes any of the parent's type parameters (but not the nested type's own)
        var angleBracketStart = nestedDeclLine.IndexOf('<');
        if(angleBracketStart < 0)
        {
            // No type params on nested type — correct (parent params not re-declared)
            return false;
        }

        var angleBracketEnd = nestedDeclLine.IndexOf('>', angleBracketStart);
        if(angleBracketEnd < 0)
        {
            return false;
        }

        var typeParamSection = nestedDeclLine.Substring(
            angleBracketStart + 1, angleBracketEnd - angleBracketStart - 1);
        var declaredParams = typeParamSection.Split(',')
            .Select(p => p.Trim())
            .ToHashSet();

        // If the nested type has its own type params, those are expected.
        // Check if any parent type params appear that are NOT the nested type's own.
        var nestedOwnParams = input.NestedTypeParams.ToHashSet();

        foreach(var parentParam in input.ParentTypeParams)
        {
            if(declaredParams.Contains(parentParam) && !nestedOwnParams.Contains(parentParam))
            {
                return true; // Parent param was re-declared — violation
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the matching closing brace for an opening brace at the given index.
    /// </summary>
    private static int FindMatchingCloseBrace(string source, int openBraceIndex)
    {
        var depth = 0;
        for(var i = openBraceIndex; i < source.Length; i++)
        {
            if(source[i] == '{')
            {
                depth++;
            }
            else if(source[i] == '}')
            {
                depth--;
            }

            if(depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Parse–print–parse round trip
    //
    // For any valid Mist source with nested types/namespaces, verify
    // parse → print → parse produces structurally identical AST.
    //
    // **Validates: Requirements 1.8, 12.5, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4: Parse–print–parse round trip.
    /// For any valid Mist source file containing nested types and/or nested namespaces,
    /// parsing the source, pretty-printing the resulting AST, then parsing the
    /// pretty-printed output SHALL produce an AST that is structurally identical
    /// to the original (same node types, same token lexemes, same tree shape),
    /// disregarding source position spans.
    ///
    /// Structural identity is verified by comparing the pretty-printed output of
    /// both ASTs: if print(AST1) == print(AST2), the ASTs are structurally identical.
    ///
    /// **Validates: Requirements 1.8, 12.5, 13.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ParsePrintParseRoundTrip_ProducesStructurallyIdenticalAst(
        RoundTripSource input)
    {
        // Step 1: Parse the generated source to get AST1
        var result1 = ParseSource(input.Source);
        if(result1.Root is not CompilationUnitNode ast1)
        {
            return false;
        }

        // No parse errors
        if(result1.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        // Step 2: Pretty-print AST1 to get printed1
        var printed1 = MistPrettyPrinter.Print(ast1, input.Source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false;
        }

        // Step 3: Parse printed1 to get AST2
        var result2 = ParseSource(printed1);
        if(result2.Root is not CompilationUnitNode ast2)
        {
            return false;
        }

        // No parse errors on the re-parsed output
        if(result2.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        // Step 4: Pretty-print AST2 to get printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify structural identity: print(AST1) == print(AST2)
        return printed1 == printed2;
    }
}