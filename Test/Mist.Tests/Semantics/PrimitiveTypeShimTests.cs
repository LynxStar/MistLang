using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Sub-task 32.1 for the Mist Standard Library spec. Validates the compat
/// shim installed by Task 32:
///
/// <list type="bullet">
/// <item>
/// Requirement 8.5 — <see cref="PrimitiveType.Int"/> and a direct
/// <c>Standard.Int32</c> lookup through
/// <see cref="SemanticModelBuilder.LookupQualifiedStdlibType"/> return
/// reference-equal <see cref="IType"/> instances. The same holds for every
/// primitive keyword in <see cref="KeywordAliases.Map"/>.
/// </item>
/// <item>
/// Requirement 8.5 — two separate Mist source occurrences of the primitive
/// keyword <c>int</c> resolve (via the semantic model's
/// <see cref="ISemanticModel.GetTypeOf"/>) to the same <see cref="IType"/>
/// instance.
/// </item>
/// </list>
///
/// <para>
/// <b>Test isolation:</b> every assertion in this class reads the
/// migration-window <see cref="PrimitiveTypeRegistry"/>, which is
/// mutated at the start and end of every
/// <see cref="SemanticModelBuilder.Build"/> call. To prevent a parallel
/// test's Build from clearing the registry mid-assertion, each test body
/// holds <see cref="PrimitiveTypeRegistry.Lock"/> for the duration of its
/// Build-plus-assertion critical section.
/// </para>
///
/// <para>
/// Validates: Requirements 8.3, 8.5, 15.3.
/// </para>
/// </summary>
public class PrimitiveTypeShimTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// Builds a semantic model whose compilation contains every stdlib
    /// bootstrap source file that declares a primitive type. Loading the
    /// real stdlib files (rather than hand-crafted minimal snippets)
    /// exercises the same registration path the driver runs in production,
    /// so the registry ends up populated with the canonical stdlib
    /// <see cref="IType"/> instances.
    /// </summary>
    private static SemanticModel BuildModelWithStdlib(string userSource)
    {
        // The SemanticModelBuilder.Build call below performs its own
        // PrimitiveTypeRegistry.Reset at the top of Build under
        // PrimitiveTypeRegistry.Lock; callers of BuildModelWithStdlib
        // are expected to wrap the Build-plus-assertion critical section
        // in `lock (PrimitiveTypeRegistry.Lock)` so the Reset happens
        // inside their lock hold and the registry stays populated for
        // the subsequent assertions.

        var stdlibRoot = FindStdlibRoot();

        // Prepend every .mist file under Source/Standard (recursively) so
        // the Standard namespace contains each primitive keyword's stdlib
        // type.
        var stdlibUnits = Directory
            .EnumerateFiles(stdlibRoot, "*.mist", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(path => new CompilationUnit(new SourceText(path, File.ReadAllText(path))) { IsStdlib = true })
            .ToArray();

        var userUnit = new CompilationUnit(new SourceText("user.mist", userSource));

        var compilation = new MistCompilation(
            stdlibUnits.Append(userUnit).ToImmutableArray());

        // Lex + parse each unit and build the AST list. We skip units with
        // lex / parse errors so the assertion-focused test output stays
        // concise; if any stdlib unit fails to parse, the test-setup
        // assertions below will surface the failure by way of the missing
        // Standard.Int32 type.
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();
        foreach (var unit in compilation.Units)
        {
            var lexBag = new DiagnosticBag();
            var tokens = Lexer.Lex(unit.Source, lexBag);
            if (lexBag.HasErrors) continue;

            var parseBag = new DiagnosticBag();
            var result = Parser.Parse(tokens, parseBag);
            if (parseBag.HasErrors || result.Root is null) continue;
            asts.Add(result.Root);
        }

        var semBag = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = (SemanticModel)builder.Build(
            compilation,
            asts.ToImmutable(),
            ambientTypeResolver: null,
            namespaceRoot: "UserProject",
            projectFolder: "/user",
            semBag);

        return model;
    }

    private static string FindStdlibRoot()
    {
        // Walk up from the test binary's base directory until we find the
        // repository root (contains Source/Standard). This avoids hard-
        // coding a relative path that depends on the current working
        // directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Source", "Standard");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Source/Standard relative to the test assembly.");
    }

    [Fact]
    public void PrimitiveTypeInt_AndLookupQualifiedStdlibType_ReturnReferenceEqualInstances()
    {
        lock (PrimitiveTypeRegistry.Lock)
        {
            // Arrange — a user program that mentions `int` once so the model
            // has at least one PrimitiveTypeRef to resolve. The body of the
            // method (and its contents) don't matter — we're asserting on the
            // shim, not on expression typing.
            var model = BuildModelWithStdlib(
                "namespace User;\npublic class Probe {\n    public static int Value() { return 0; }\n}\n");

            // The stdlib Standard.Int32 type obtained through the direct
            // stdlib lookup helper.
            var stdlibInt32 = SemanticModelBuilder.LookupQualifiedStdlibType(
                (Scope)model.GlobalScope,
                model.SymbolTable,
                KeywordAliases.Map["int"]);

            // Act + Assert — every accessor surfaces the same IType instance.
            Assert.Same(stdlibInt32, PrimitiveType.Int);
            Assert.IsType<StructType>(PrimitiveType.Int);
            var structType = (StructType)PrimitiveType.Int;
            Assert.Equal("Int32", structType.DisplayName);
            Assert.Equal("Standard", structType.ContainingNamespace);
        }
    }

    [Fact]
    public void AllPrimitiveAccessors_ReturnReferenceEqualStdlibInstances()
    {
        lock (PrimitiveTypeRegistry.Lock)
        {
            // Arrange — any user program that forces semantic model
            // construction so the registry is populated.
            var model = BuildModelWithStdlib(
                "namespace User;\npublic class Probe { }\n");
            var globalScope = (Scope)model.GlobalScope;

            // Act + Assert — walk every alias entry and verify the compat
            // shim accessor matches the direct stdlib lookup.
            AssertReferenceEqual(globalScope, model.SymbolTable, "int", PrimitiveType.Int);
            AssertReferenceEqual(globalScope, model.SymbolTable, "uint", PrimitiveType.Uint);
            AssertReferenceEqual(globalScope, model.SymbolTable, "long", PrimitiveType.Long);
            AssertReferenceEqual(globalScope, model.SymbolTable, "ulong", PrimitiveType.Ulong);
            AssertReferenceEqual(globalScope, model.SymbolTable, "short", PrimitiveType.Short);
            AssertReferenceEqual(globalScope, model.SymbolTable, "ushort", PrimitiveType.Ushort);
            AssertReferenceEqual(globalScope, model.SymbolTable, "byte", PrimitiveType.Byte);
            AssertReferenceEqual(globalScope, model.SymbolTable, "sbyte", PrimitiveType.Sbyte);
            AssertReferenceEqual(globalScope, model.SymbolTable, "float", PrimitiveType.Float);
            AssertReferenceEqual(globalScope, model.SymbolTable, "double", PrimitiveType.Double);
            AssertReferenceEqual(globalScope, model.SymbolTable, "decimal", PrimitiveType.Decimal);
            AssertReferenceEqual(globalScope, model.SymbolTable, "bool", PrimitiveType.Bool);
            AssertReferenceEqual(globalScope, model.SymbolTable, "char", PrimitiveType.Char);
            AssertReferenceEqual(globalScope, model.SymbolTable, "string", StringType.Instance);
        }
    }

    private static void AssertReferenceEqual(
        Scope globalScope,
        SymbolTable symbolTable,
        string keyword,
        IType accessorValue)
    {
        var qualifiedName = KeywordAliases.Map[keyword];
        var stdlibType = SemanticModelBuilder.LookupQualifiedStdlibType(
            globalScope, symbolTable, qualifiedName);
        Assert.Same(stdlibType, accessorValue);
    }

    [Fact]
    public void TwoSeparateIntOccurrences_ResolveToSameIType()
    {
        lock (PrimitiveTypeRegistry.Lock)
        {
            // Arrange — a user program with two distinct source occurrences
            // of the `int` keyword. Both the parameter type and the return
            // type use the primitive-keyword form; they are parsed as
            // separate PrimitiveTypeRefTypeReferenceNode instances, so a
            // naive implementation that forgot to share the stdlib instance
            // would hand out two distinct IType objects.
            var model = BuildModelWithStdlib(
                "namespace User;\npublic class Probe {\n" +
                "    public static int Identity(int value) { return value; }\n" +
                "}\n");

            // The model's method symbol exposes its resolved return type
            // (first `int`) and its parameter signature (second `int`).
            var methodSymbol = model.AllSymbols.FirstOrDefault(s =>
                s.Name == "Identity" && s.Kind == SymbolKind.Method);
            Assert.NotNull(methodSymbol);

            // Return type of the method — resolved via Pass 2 through
            // ResolvePrimitiveKeyword (Task 31 flip).
            var returnType = methodSymbol!.DeclaredType;
            Assert.NotNull(returnType);

            // Parameter type of `value: int` — extracted the same way Pass 2
            // registered it.
            var paramTypes = ParameterSignature.Extract(methodSymbol, model);
            Assert.Single(paramTypes);
            var paramType = paramTypes[0];

            // Act + Assert — both occurrences are reference-equal
            // (Requirement 8.5).
            Assert.Same(returnType, paramType);

            // And both are reference-equal to the compat-shim accessor,
            // confirming end-to-end consistency across the resolution path,
            // the registry, and the PrimitiveType property.
            Assert.Same(PrimitiveType.Int, returnType);
            Assert.Same(PrimitiveType.Int, paramType);
        }
    }
}
