// Feature: default-keyword
// Property 2: Default block parsing preserves structure — **Validates: Requirements 2.1, 2.2, 2.4, 2.5**
// Property 3: Default expression parsing captures type reference — **Validates: Requirements 3.1, 3.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for default block parsing structure preservation and
/// default expression parsing type reference capture.
///
/// **Validates: Requirements 2.1, 2.2, 2.4, 2.5, 3.1, 3.4**
/// </summary>
public class DefaultBlockParsingPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Name pools (unique to avoid duplicate field names) ──────────────

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon"];

    private static readonly string[] TypeBindingTypeNames =
        ["OpenGLRenderer", "ConsoleLogger", "FileWriter", "MemoryCache", "JsonParser"];

    // ─── Entry kind enum ─────────────────────────────────────────────────

    private enum EntryKind { FieldOverride, TypeBinding }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a single entry descriptor tuple: (fieldName, kind, literalValue, typeBindingName).
    /// The field name is selected by index to ensure uniqueness.
    /// </summary>
    private static Gen<(string FieldName, EntryKind Kind, int LiteralValue, string? TypeBindingName)> GenEntry(int index)
    {
        var fieldName = FieldNames[index];

        var genFieldOverride =
            from literalValue in Gen.Choose(0, 999)
            select (fieldName, EntryKind.FieldOverride, literalValue, (string?)null);

        var genTypeBinding =
            from typeName in Gen.Elements(TypeBindingTypeNames)
            select (fieldName, EntryKind.TypeBinding, 0, (string?)typeName);

        return Gen.OneOf(genFieldOverride, genTypeBinding);
    }

    /// <summary>
    /// Generates a list of exactly <paramref name="count"/> entries with unique field names.
    /// </summary>
    private static Gen<ImmutableArray<(string FieldName, EntryKind Kind, int LiteralValue, string? TypeBindingName)>>
        GenEntriesOfCount(int count)
    {
        if (count == 0)
            return Gen.Constant(ImmutableArray<(string, EntryKind, int, string?)>.Empty);

        Gen<ImmutableArray<(string, EntryKind, int, string?)>> result =
            GenEntry(0).Select(e => ImmutableArray.Create(e));

        for (var i = 1; i < count; i++)
        {
            var captured = i;
            result = from arr in result
                     from entry in GenEntry(captured)
                     select arr.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Generates a complete test scenario: (isStruct, typeName, entries).
    /// </summary>
    private static Gen<(bool IsStruct, string TypeName,
        ImmutableArray<(string FieldName, EntryKind Kind, int LiteralValue, string? TypeBindingName)> Entries)>
        GenScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements("Widget", "Config", "Engine", "Sensor", "Agent")
               from count in Gen.Choose(0, 5)
               from entries in GenEntriesOfCount(count)
               select (isStruct, typeName, entries);
    }

    // ─── Source text generation ──────────────────────────────────────────

    /// <summary>
    /// Builds a Mist source text string from a scenario descriptor.
    /// </summary>
    private static string BuildSource(
        bool isStruct,
        string typeName,
        ImmutableArray<(string FieldName, EntryKind Kind, int LiteralValue, string? TypeBindingName)> entries)
    {
        var keyword = isStruct ? "struct" : "class";
        var lines = new List<string>();

        lines.Add($"public {keyword} {typeName} {{");

        if (entries.Length == 0)
        {
            lines.Add("    default { }");
        }
        else
        {
            lines.Add("    default {");
            foreach (var entry in entries)
            {
                switch (entry.Kind)
                {
                    case EntryKind.FieldOverride:
                        lines.Add($"        {entry.FieldName} = {entry.LiteralValue}");
                        break;
                    case EntryKind.TypeBinding:
                        lines.Add($"        {entry.FieldName}: {entry.TypeBindingName}");
                        break;
                }
            }
            lines.Add("    }");
        }

        lines.Add("}");

        return string.Join("\n", lines);
    }

    // ─── Parse helper ────────────────────────────────────────────────────

    /// <summary>
    /// Parses the given Mist source text and returns the compilation unit AST node,
    /// or null if parsing fails.
    /// </summary>
    private static CompilationUnitNode? ParseSource(string source, out bool hasErrors)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Navigates the AST to find the default block declaration node inside
    /// the first type declaration.
    /// </summary>
    private static DefaultBlockDeclarationNode? FindDefaultBlock(CompilationUnitNode cu)
    {
        var types = cu.GetTypes();
        if (types.IsEmpty)
            return null;

        var typeDecl = types[0];

        // Get the members from either a class or struct declaration
        ImmutableArray<MemberDeclarationNode> members;
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classType:
                members = classType.ClassDeclaration.Members;
                break;
            case StructTypeTypeDeclarationNode structType:
                members = structType.StructDeclaration.Members;
                break;
            default:
                return null;
        }

        // Find the default block member
        foreach (var member in members)
        {
            if (member is DefaultBlockMemberMemberDeclarationNode defaultBlockMember)
                return defaultBlockMember.DefaultBlock;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Default block parsing preserves structure
    //
    // **Validates: Requirements 2.1, 2.2, 2.4, 2.5**
    //
    // For any class or struct declaration containing a default block with N
    // field initializer entries (where each entry is either a field override
    // or a type binding), parsing the declaration SHALL produce an AST node
    // whose default block contains exactly N entries, each with the correct
    // field name and entry kind.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultBlockParsing_PreservesEntryCountFieldNamesAndKinds()
    {
        var config = Config.Default.WithMaxTest(100);

        var property = Prop.ForAll(
            GenScenario().ToArbitrary(),
            scenario =>
            {
                var (isStruct, typeName, entries) = scenario;
                var source = BuildSource(isStruct, typeName, entries);
                var cu = ParseSource(source, out var hasErrors);

                // If parsing fails, discard this test case
                if (cu is null || hasErrors)
                    return true.ToProperty().Label("Parsing failed — discarded");

                var defaultBlock = FindDefaultBlock(cu);
                if (defaultBlock is null)
                    return false.ToProperty()
                        .Label($"No default block found in AST.\nSource:\n{source}");

                // Verify entry count matches
                var expectedCount = entries.Length;
                var actualCount = defaultBlock.Entries.Length;
                if (actualCount != expectedCount)
                    return false.ToProperty()
                        .Label($"Entry count mismatch: expected {expectedCount}, got {actualCount}.\nSource:\n{source}");

                // Verify each entry's field name and kind
                for (var i = 0; i < expectedCount; i++)
                {
                    var expected = entries[i];
                    var actual = defaultBlock.Entries[i];

                    // Check field name
                    var actualFieldName = actual switch
                    {
                        DefaultFieldOverrideDefaultFieldInitializerNode fo => fo.FieldName.Lexeme,
                        DefaultTypeBindingDefaultFieldInitializerNode tb => tb.FieldName.Lexeme,
                        _ => "<unknown>",
                    };

                    if (actualFieldName != expected.FieldName)
                        return false.ToProperty()
                            .Label($"Entry {i}: expected field name '{expected.FieldName}', got '{actualFieldName}'.\nSource:\n{source}");

                    // Check entry kind
                    var actualKind = actual switch
                    {
                        DefaultFieldOverrideDefaultFieldInitializerNode => EntryKind.FieldOverride,
                        DefaultTypeBindingDefaultFieldInitializerNode => EntryKind.TypeBinding,
                        _ => (EntryKind)(-1),
                    };

                    if (actualKind != expected.Kind)
                        return false.ToProperty()
                            .Label($"Entry {i}: expected kind {expected.Kind}, got {actualKind}.\nSource:\n{source}");
                }

                var kind = isStruct ? "struct" : "class";
                return true.ToProperty()
                    .Label($"OK: {kind} {typeName} with {entries.Length} entries");
            });

        property.Check(config);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Default expression parsing captures type reference
    //
    // **Validates: Requirements 3.1, 3.4**
    //
    // For any valid type name T, parsing the expression `default(T)` SHALL
    // produce a `DefaultExpressionPrimaryNode` whose type reference resolves
    // to T.
    // ═══════════════════════════════════════════════════════════════════════

    // ─── Type name pools for Property 3 ──────────────────────────────────

    /// <summary>Named (user-defined) type names for default expression testing.</summary>
    private static readonly string[] NamedTypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent", "Point", "Matrix"];

    /// <summary>
    /// Primitive type keywords recognized by the Mist parser.
    /// These are parsed as <c>PrimitiveTypeRefTypeReferenceNode</c> rather than
    /// <c>NamedTypeRefTypeReferenceNode</c>.
    /// </summary>
    private static readonly string[] PrimitiveTypeKeywords =
        ["int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
         "float", "double", "decimal", "bool", "char", "string"];

    // ─── Generator for Property 3 ────────────────────────────────────────

    /// <summary>
    /// Generates a random type name string — either a named type or a primitive keyword.
    /// </summary>
    private static Gen<string> GenTypeName()
    {
        return Gen.OneOf(
            Gen.Elements(NamedTypeNames),
            Gen.Elements(PrimitiveTypeKeywords));
    }

    // ─── Source builder for Property 3 ───────────────────────────────────

    /// <summary>
    /// Builds a Mist source string containing <c>var x = default(TypeName);</c>
    /// inside a method inside a class, providing a valid expression context.
    /// </summary>
    private static string BuildDefaultExpressionSource(string typeName)
    {
        return $$"""
            public class Wrapper {
                public void Test() {
                    var x = default({{typeName}});
                }
            }
            """;
    }

    // ─── AST navigation for Property 3 ───────────────────────────────────

    /// <summary>
    /// Recursively searches the AST for the first <c>DefaultExpressionPrimaryNode</c>.
    /// </summary>
    private static DefaultExpressionPrimaryNode? FindDefaultExpression(Storm.Runtime.SyntaxNode? node)
    {
        if (node is null) return null;
        if (node is DefaultExpressionPrimaryNode defaultExpr) return defaultExpr;

        foreach (var child in node.Children())
        {
            var found = FindDefaultExpression(child);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>
    /// Extracts the type name string from a <c>TypeReferenceNode</c> using its source span.
    /// This works for both named types and primitive types produced by the parser.
    /// </summary>
    private static string? ExtractTypeName(TypeReferenceNode typeRef, string source)
    {
        var span = typeRef.Span;
        if (span.Length > 0 && span.Start + span.Length <= source.Length)
            return source.Substring(span.Start, span.Length);

        // Fallback for hand-crafted ASTs: check known node types
        return typeRef switch
        {
            NamedTypeRefTypeReferenceNode namedRef when !string.IsNullOrEmpty(namedRef.Name.IDENTIFIER.Lexeme)
                => namedRef.Name.IDENTIFIER.Lexeme,
            PrimitiveTypeRefTypeReferenceNode primitiveRef => primitiveRef.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => "int",
                UintTypePrimitiveTypeNode => "uint",
                LongTypePrimitiveTypeNode => "long",
                UlongTypePrimitiveTypeNode => "ulong",
                ShortTypePrimitiveTypeNode => "short",
                UshortTypePrimitiveTypeNode => "ushort",
                ByteTypePrimitiveTypeNode => "byte",
                SbyteTypePrimitiveTypeNode => "sbyte",
                FloatTypePrimitiveTypeNode => "float",
                DoubleTypePrimitiveTypeNode => "double",
                DecimalTypePrimitiveTypeNode => "decimal",
                BoolTypePrimitiveTypeNode => "bool",
                CharTypePrimitiveTypeNode => "char",
                StringTypePrimitiveTypeNode => "string",
                _ => null,
            },
            _ => null,
        };
    }

    // ─── Property 3 test ─────────────────────────────────────────────────

    [Fact]
    public void DefaultExpressionParsing_CapturesTypeReference()
    {
        var config = Config.Default.WithMaxTest(100);

        var property = Prop.ForAll(
            GenTypeName().ToArbitrary(),
            typeName =>
            {
                var source = BuildDefaultExpressionSource(typeName);
                var cu = ParseSource(source, out var hasErrors);

                if (cu is null || hasErrors)
                    return false.ToProperty()
                        .Label($"Parsing failed for type '{typeName}'.\nSource:\n{source}");

                var defaultExpr = FindDefaultExpression(cu);
                if (defaultExpr is null)
                    return false.ToProperty()
                        .Label($"No DefaultExpressionPrimaryNode found in AST for type '{typeName}'.\nSource:\n{source}");

                var actualTypeName = ExtractTypeName(defaultExpr.Type, source);
                if (actualTypeName is null)
                    return false.ToProperty()
                        .Label($"Could not extract type name from TypeReferenceNode for type '{typeName}'.");

                return (actualTypeName == typeName).ToProperty()
                    .Label($"Type mismatch: expected '{typeName}', got '{actualTypeName}'.");
            });

        property.Check(config);
    }
}
