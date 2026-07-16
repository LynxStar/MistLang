// Feature: full-accessibility-model, Property 9: Internal members respect the compilation-as-assembly boundary
//
// **Validates: Requirements 6.3, 6.4, 7.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that internal members respect the compilation-as-assembly
/// boundary.
///
/// Property 9: For any Internal member, a reference from elsewhere within the same
/// compilation is accepted without an accessibility diagnostic, and a reference from
/// an External_Assembly is reported as an accessibility-violation error (MIST_TC_0172).
///
/// The same-compilation case uses in-process Lex -> Parse -> SemanticModel -> TypeChecker
/// with no file I/O. The cross-assembly case uses CompilerDriver.Compile with on-disk
/// project references to establish External_Assembly boundaries.
///
/// **Validates: Requirements 6.3, 6.4, 7.3**
/// </summary>
public sealed class InternalCompilationBoundaryPropertyTests : IDisposable
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string scratchDir;

    public InternalCompilationBoundaryPropertyTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-internal-boundary-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario for testing internal member access within the same compilation.
    /// </summary>
    public sealed record SameCompilationScenario(
        string Source,
        string Description)
    {
        public override string ToString()
        {
            return $"[ACCEPT] {Description}";
        }
    }

    /// <summary>
    /// Represents a scenario for testing internal member access across the compilation boundary.
    /// </summary>
    public sealed record CrossAssemblyScenario(
        string LibrarySource,
        string ConsumerSource,
        string Description)
    {
        public override string ToString()
        {
            return $"[REJECT] {Description}";
        }
    }

#pragma warning restore CA1034

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles source in-process (Lex -> Parse -> SemanticModel -> TypeChecker)
    /// and returns all diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileInProcess(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return lexBag.ToImmutableArray()
            .AddRange(parseBag.ToImmutableArray())
            .AddRange(semBag.ToImmutableArray())
            .AddRange(tcBag.ToImmutableArray());
    }

    /// <summary>
    /// Creates a project directory with a mist.toml and source files on disk.
    /// Returns the absolute path to the project directory.
    /// </summary>
    private string CreateProject(
        string name,
        string? namespaceRoot = null,
        string[]? referencePaths = null,
        Dictionary<string, string>? sourceFiles = null)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);

        var toml = $"name = \"{name}\"\n";
        if(namespaceRoot is not null)
        {
            toml += $"namespace_root = \"{namespaceRoot}\"\n";
        }

        if(referencePaths is not null)
        {
            toml += "\n[references]\n";
            foreach(var refPath in referencePaths)
            {
                var escapedPath = refPath.Replace("\\", "/");
                var trimmedPath = refPath.TrimEnd('/', '\\');
                var fileName = Path.GetFileName(trimmedPath);
                var refName = string.Equals(fileName, "mist.toml", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(Path.GetDirectoryName(trimmedPath)!)
                    : fileName;
                toml += $"{refName} = {{ path = \"{escapedPath}\" }}\n";
            }
        }

        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

        if(sourceFiles is not null)
        {
            foreach(var (fileName, content) in sourceFiles)
            {
                File.WriteAllText(Path.Combine(projectDir, fileName), content);
            }
        }

        return projectDir;
    }

    /// <summary>
    /// Compiles a project using the shared CompilerDriver (file-based, for cross-assembly tests).
    /// </summary>
    private static CompilerResult CompileProject(string projectDir, string nameRoot)
    {
        var sourcePaths = Directory.GetFiles(projectDir, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: nameRoot,
            NamespaceRoot: nameRoot,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    private static bool HasInternalCrossAssemblyError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Id.Value == "MIST_TC_0172");
    }

    // ─── Generators: Same-compilation (ACCEPT) ──────────────────────────

    private static readonly string[] MemberNames =
        ["value", "count", "data", "flag", "item"];

    private static readonly string[] TypeNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    /// <summary>
    /// Generates: internal field accessed from a different class in the same compilation.
    /// Requirement 6.3: internal is visible within the same compilation.
    /// </summary>
    private static Gen<SameCompilationScenario> GenInternalFieldSameCompilation()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new SameCompilationScenario(
                   Source: $@"
public class {typeName} {{
    internal int {fieldName};
    public {typeName}() {{
        this.{fieldName} = 42;
    }}
}}
public class Consumer {{
    public int Read({typeName} obj) {{
        return obj.{fieldName};
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal field {typeName}.{fieldName} accessed from Consumer in same compilation");
    }

    /// <summary>
    /// Generates: internal method accessed from a different class in the same compilation.
    /// Requirement 6.3: internal is visible within the same compilation.
    /// </summary>
    private static Gen<SameCompilationScenario> GenInternalMethodSameCompilation()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MemberNames)
               select new SameCompilationScenario(
                   Source: $@"
public class {typeName} {{
    internal int {methodName}() {{
        return 42;
    }}
    public static void Main() {{ }}
}}
public class Consumer {{
    public int Call({typeName} obj) {{
        return obj.{methodName}();
    }}
}}",
                   Description: $"Internal method {typeName}.{methodName}() accessed from Consumer in same compilation");
    }

    /// <summary>
    /// Generates: internal static field accessed from a different class via type name
    /// in the same compilation.
    /// Requirement 6.3: internal is visible within the same compilation.
    /// </summary>
    private static Gen<SameCompilationScenario> GenInternalStaticFieldSameCompilation()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new SameCompilationScenario(
                   Source: $@"
public class {typeName} {{
    internal static int {fieldName};
    public static void Main() {{ }}
}}
public class Consumer {{
    public int Read() {{
        return {typeName}.{fieldName};
    }}
}}",
                   Description: $"Internal static field {typeName}.{fieldName} accessed from Consumer in same compilation");
    }

    /// <summary>
    /// Generates: internal static method accessed from a different class via type name
    /// in the same compilation.
    /// Requirement 6.3: internal is visible within the same compilation.
    /// </summary>
    private static Gen<SameCompilationScenario> GenInternalStaticMethodSameCompilation()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MemberNames)
               select new SameCompilationScenario(
                   Source: $@"
public class {typeName} {{
    internal static int {methodName}() {{
        return 42;
    }}
    public static void Main() {{ }}
}}
public class Consumer {{
    public int Call() {{
        return {typeName}.{methodName}();
    }}
}}",
                   Description: $"Internal static method {typeName}.{methodName}() accessed from Consumer in same compilation");
    }

    /// <summary>
    /// Generates: internal field on a struct accessed from a different type in the same
    /// compilation.
    /// Requirement 6.3: internal is visible within the same compilation.
    /// </summary>
    private static Gen<SameCompilationScenario> GenInternalStructFieldSameCompilation()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new SameCompilationScenario(
                   Source: $@"
public struct {typeName} {{
    internal int {fieldName};
    public {typeName}(int v) {{
        this.{fieldName} = v;
    }}
}}
public class Consumer {{
    public int Read({typeName} obj) {{
        return obj.{fieldName};
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal struct field {typeName}.{fieldName} accessed from Consumer in same compilation");
    }

    private static Gen<SameCompilationScenario> GenSameCompilationScenario()
    {
        return Gen.OneOf(
            GenInternalFieldSameCompilation(),
            GenInternalMethodSameCompilation(),
            GenInternalStaticFieldSameCompilation(),
            GenInternalStaticMethodSameCompilation(),
            GenInternalStructFieldSameCompilation());
    }

    public static Arbitrary<SameCompilationScenario> ArbitrarySameCompilationScenario()
    {
        return Arb.From(GenSameCompilationScenario());
    }

    // ─── Generators: Cross-assembly (REJECT) ─────────────────────────────

    /// <summary>
    /// Generates: internal static field in a library project, accessed from a consumer project
    /// via a static member access expression (TypeName.field).
    /// Requirement 6.4: internal across External_Assembly is rejected.
    /// </summary>
    private static Gen<CrossAssemblyScenario> GenInternalStaticFieldCrossAssembly()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new CrossAssemblyScenario(
                   LibrarySource: $@"
namespace SharedLib;

public class {typeName} {{
    internal static int {fieldName};
}}",
                   ConsumerSource: $@"
using SharedLib;

namespace MyApp;

public class Consumer {{
    public int Read() {{
        return {typeName}.{fieldName};
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal static field SharedLib.{typeName}.{fieldName} accessed across assembly boundary");
    }

    /// <summary>
    /// Generates: internal static method in a library project, accessed from a consumer project
    /// via a static method call expression (TypeName.method()).
    /// Requirement 6.4: internal across External_Assembly is rejected.
    /// </summary>
    private static Gen<CrossAssemblyScenario> GenInternalStaticMethodCrossAssembly()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new CrossAssemblyScenario(
                   LibrarySource: $@"
namespace SharedLib;

public class {typeName} {{
    internal static int {fieldName}() {{
        return 42;
    }}
}}",
                   ConsumerSource: $@"
using SharedLib;

namespace MyApp;

public class Consumer {{
    public int Call() {{
        return {typeName}.{fieldName}();
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal static method SharedLib.{typeName}.{fieldName}() accessed across assembly boundary");
    }

    /// <summary>
    /// Generates: internal instance field in a library project, accessed from a consumer project
    /// by constructing an object and reading the field.
    /// Requirement 6.4: internal across External_Assembly is rejected.
    /// </summary>
    private static Gen<CrossAssemblyScenario> GenInternalFieldCrossAssembly()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new CrossAssemblyScenario(
                   LibrarySource: $@"
namespace SharedLib;

public class {typeName} {{
    internal int {fieldName};
    public {typeName}() {{
        this.{fieldName} = 42;
    }}
}}",
                   ConsumerSource: $@"
using SharedLib;

namespace MyApp;

public class Consumer {{
    public int Read() {{
        var obj = new {typeName}();
        return obj.{fieldName};
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal field SharedLib.{typeName}.{fieldName} accessed across assembly boundary");
    }

    /// <summary>
    /// Generates: internal instance method in a library project, accessed from a consumer project
    /// by constructing an object and calling the method.
    /// Requirement 6.4: internal across External_Assembly is rejected.
    /// </summary>
    private static Gen<CrossAssemblyScenario> GenInternalMethodCrossAssembly()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new CrossAssemblyScenario(
                   LibrarySource: $@"
namespace SharedLib;

public class {typeName} {{
    internal int {fieldName}() {{
        return 42;
    }}
}}",
                   ConsumerSource: $@"
using SharedLib;

namespace MyApp;

public class Consumer {{
    public int Call() {{
        var obj = new {typeName}();
        return obj.{fieldName}();
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal method SharedLib.{typeName}.{fieldName}() accessed across assembly boundary");
    }

    /// <summary>
    /// Generates: internal struct field in a library project, accessed from a consumer project.
    /// Requirement 6.4: internal across External_Assembly is rejected.
    /// </summary>
    private static Gen<CrossAssemblyScenario> GenInternalStructFieldCrossAssembly()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(MemberNames)
               select new CrossAssemblyScenario(
                   LibrarySource: $@"
namespace SharedLib;

public struct {typeName} {{
    internal int {fieldName};
    public {typeName}(int v) {{
        this.{fieldName} = v;
    }}
}}",
                   ConsumerSource: $@"
using SharedLib;

namespace MyApp;

public class Consumer {{
    public int Read() {{
        var obj = new {typeName}(7);
        return obj.{fieldName};
    }}
    public static void Main() {{ }}
}}",
                   Description: $"Internal struct field SharedLib.{typeName}.{fieldName} accessed across assembly boundary");
    }

    private static Gen<CrossAssemblyScenario> GenCrossAssemblyScenario()
    {
        return Gen.OneOf(
            GenInternalStaticFieldCrossAssembly(),
            GenInternalStaticMethodCrossAssembly(),
            GenInternalFieldCrossAssembly(),
            GenInternalMethodCrossAssembly(),
            GenInternalStructFieldCrossAssembly());
    }

    public static Arbitrary<CrossAssemblyScenario> ArbitraryCrossAssemblyScenario()
    {
        return Arb.From(GenCrossAssemblyScenario());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Internal members respect the compilation-as-assembly boundary
    //
    // For any Internal member, a reference from elsewhere within the same
    // compilation is accepted without an accessibility diagnostic, and a
    // reference from an External_Assembly is reported as an accessibility-
    // violation error (MIST_TC_0172).
    //
    // **Validates: Requirements 6.3, 6.4, 7.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Same-compilation: internal members are accessible from any type within
    /// the same compilation (Req 6.3). No MIST_TC_0172 should be emitted.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InternalCompilationBoundaryPropertyTests) })]
    public Property InternalMember_SameCompilation_IsAccepted(SameCompilationScenario scenario)
    {
        var diagnostics = CompileInProcess(scenario.Source);
        var hasInternalError = HasInternalCrossAssemblyError(diagnostics);

        return (!hasInternalError).ToProperty()
            .Label($"Expected ACCEPT (no MIST_TC_0172) for: {scenario.Description}" +
                   (hasInternalError
                       ? $"\nUnexpected diagnostics: {string.Join("; ", diagnostics.Where(d => d.Id.Value == "MIST_TC_0172").Select(d => $"{d.Id.Value}: {d.Message}"))}"
                       : ""));
    }

    /// <summary>
    /// Cross-assembly: internal members are NOT accessible from an External_Assembly
    /// (Req 6.4, 7.3). MIST_TC_0172 must be emitted.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InternalCompilationBoundaryPropertyTests) })]
    public Property InternalMember_CrossAssembly_IsRejected(CrossAssemblyScenario scenario)
    {
        // Create the library project (SharedLib) with an internal member
        var libDir = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            sourceFiles: new Dictionary<string, string>
            {
                ["Lib.mist"] = scenario.LibrarySource
            });

        // Create the consumer project (MyApp) that references the library
        var consumerDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [libDir],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = scenario.ConsumerSource
            });

        var result = CompileProject(consumerDir, "MyApp");
        var hasInternalError = HasInternalCrossAssemblyError(result.Diagnostics);

        return hasInternalError.ToProperty()
            .Label($"Expected REJECT (MIST_TC_0172) for: {scenario.Description}" +
                   (!hasInternalError
                       ? $"\nAll diagnostics: {string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}"
                       : ""));
    }
}