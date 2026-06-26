// Feature: optional-defaultability, Property 10: __CreateDefault emission gating
// Property 10: __CreateDefault emission is gated by defaultability — **Validates: Requirements 12.1, 12.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

// ─── Scenario types ─────────────────────────────────────────────────────────

#pragma warning disable CA1034

/// <summary>
/// Scenario for a type that has a constructor and no default block (NotDefaultable).
/// The emitted C# SHALL NOT contain a __CreateDefault() method for this type.
/// </summary>
public sealed record NotDefaultableEmissionScenario(
    string Source,
    string TypeName,
    bool IsStruct,
    int FieldCount)
{
    public override string ToString()
    {
        return $"{(IsStruct ? "struct" : "class")} {TypeName} + ctor, no default block ({FieldCount} fields) → NotDefaultable";
    }
}

/// <summary>
/// Scenario for a type that has no constructor and all defaultable fields (Inferred).
/// The emitted C# SHALL contain a __CreateDefault() method for this type.
/// </summary>
public sealed record InferredDefaultableEmissionScenario(
    string Source,
    string TypeName,
    bool IsStruct,
    int FieldCount)
{
    public override string ToString()
    {
        return $"{(IsStruct ? "struct" : "class")} {TypeName}, no ctor, all defaultable fields ({FieldCount} fields) → Inferred";
    }
}

/// <summary>
/// Scenario for a type that has a default block (Explicit).
/// The emitted C# SHALL contain a __CreateDefault() method for this type.
/// </summary>
public sealed record ExplicitDefaultableEmissionScenario(
    string Source,
    string TypeName,
    bool IsStruct,
    int FieldCount)
{
    public override string ToString()
    {
        return $"{(IsStruct ? "struct" : "class")} {TypeName} + default block ({FieldCount} fields) → Explicit";
    }
}

#pragma warning restore CA1034

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for __CreateDefault emission gating by defaultability status.
///
/// Property 10: For any type classified as NotDefaultable, the emitted C# code SHALL NOT
/// contain a __CreateDefault() method for that type. For any type classified as Inferred
/// or Explicit, the emitted C# SHALL contain a __CreateDefault() method.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
///
/// **Validates: Requirements 12.1, 12.2**
/// </summary>
public sealed class CreateDefaultEmissionPropertyTests : IDisposable
{
    private readonly string outputDir;

    public CreateDefaultEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistCreateDefaultEmissionPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver ───────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray<IAnalyzer>.Empty,
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Gets all emitted C# content concatenated (excluding .csproj and MistVoid.g.cs).
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
    /// Checks whether the emitted C# contains a __CreateDefault method for the given type name.
    /// Looks for the pattern: public static TypeName __CreateDefault()
    /// </summary>
    private static bool HasCreateDefaultForType(string emittedCSharp, string typeName)
    {
        // Match pattern: "public static TypeName __CreateDefault()"
        // The type name appears as the return type of the factory method
        return emittedCSharp.Contains($"static {typeName} __CreateDefault()");
    }

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "double"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon"];

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildNotDefaultableSource(
        bool isStruct, string typeName, ImmutableArray<(string FieldName, string FieldType)> fields)
    {
        var kind = isStruct ? "struct" : "class";
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("namespace TestProgram;");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"public {kind} {typeName}");
        _ = sb.AppendLine("{");
        foreach(var (fieldName, fieldType) in fields)
        {
            _ = sb.AppendLine($"    public {fieldType} {fieldName};");
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine($"    public {typeName}()");
        _ = sb.AppendLine("    {");
        foreach(var (fieldName, fieldType) in fields)
        {
            var defaultValue = fieldType switch
            {
                "int" => "0",
                "bool" => "false",
                "double" => "0.0",
                "string" => "\"\"",
                _ => "0",
            };
            _ = sb.AppendLine($"        this.{fieldName} = {defaultValue};");
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("public class Program");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public static void Main()");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    private static string BuildInferredDefaultableSource(
        bool isStruct, string typeName, ImmutableArray<(string FieldName, string FieldType)> fields)
    {
        var kind = isStruct ? "struct" : "class";
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("namespace TestProgram;");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"public {kind} {typeName}");
        _ = sb.AppendLine("{");
        foreach(var (fieldName, fieldType) in fields)
        {
            _ = sb.AppendLine($"    public {fieldType} {fieldName};");
        }

        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("public class Program");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public static void Main()");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    private static string BuildExplicitDefaultableSource(
        bool isStruct, string typeName, ImmutableArray<(string FieldName, string FieldType)> fields)
    {
        var kind = isStruct ? "struct" : "class";
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("namespace TestProgram;");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"public {kind} {typeName}");
        _ = sb.AppendLine("{");
        foreach(var (fieldName, fieldType) in fields)
        {
            _ = sb.AppendLine($"    public {fieldType} {fieldName};");
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine("    default");
        _ = sb.AppendLine("    {");
        foreach(var (fieldName, fieldType) in fields)
        {
            var defaultValue = fieldType switch
            {
                "int" => "0",
                "bool" => "false",
                "double" => "0.0",
                "string" => "\"\"",
                _ => "0",
            };
            _ = sb.AppendLine($"        {fieldName} = {defaultValue}");
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("public class Program");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public static void Main()");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<ImmutableArray<(string FieldName, string FieldType)>> GenFields(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(ImmutableArray<(string, string)>.Empty);
        }

        var result =
            from fieldType in Gen.Elements(PrimitiveTypes)
            select ImmutableArray.Create<(string, string)>((FieldNames[0], fieldType));

        for(var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from fieldType in Gen.Elements(PrimitiveTypes)
                     select arr.Add((FieldNames[idx], fieldType));
        }

        return result;
    }

    private static Gen<NotDefaultableEmissionScenario> GenNotDefaultableScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenFields(fieldCount)
               let source = BuildNotDefaultableSource(isStruct, typeName, fields)
               select new NotDefaultableEmissionScenario(source, typeName, isStruct, fieldCount);
    }

    private static Gen<InferredDefaultableEmissionScenario> GenInferredDefaultableScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenFields(fieldCount)
               let source = BuildInferredDefaultableSource(isStruct, typeName, fields)
               select new InferredDefaultableEmissionScenario(source, typeName, isStruct, fieldCount);
    }

    private static Gen<ExplicitDefaultableEmissionScenario> GenExplicitDefaultableScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenFields(fieldCount)
               let source = BuildExplicitDefaultableSource(isStruct, typeName, fields)
               select new ExplicitDefaultableEmissionScenario(source, typeName, isStruct, fieldCount);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<NotDefaultableEmissionScenario> ArbitraryNotDefaultable()
    {
        return GenNotDefaultableScenario().ToArbitrary();
    }

    public static Arbitrary<InferredDefaultableEmissionScenario> ArbitraryInferredDefaultable()
    {
        return GenInferredDefaultableScenario().ToArbitrary();
    }

    public static Arbitrary<ExplicitDefaultableEmissionScenario> ArbitraryExplicitDefaultable()
    {
        return GenExplicitDefaultableScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: __CreateDefault emission is gated by defaultability
    //
    // **Validates: Requirements 12.1, 12.2**
    //
    // For any type classified as NotDefaultable, the emitted C# code SHALL NOT
    // contain a __CreateDefault() method for that type.
    // For any type classified as Inferred or Explicit, the emitted C# SHALL
    // contain a __CreateDefault() method.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 10a: Types with constructors and no default block (NotDefaultable)
    /// SHALL NOT have __CreateDefault() in the emitted C#.
    ///
    /// **Validates: Requirements 12.1**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(CreateDefaultEmissionPropertyTests) })]
    public Property NotDefaultableType_DoesNotEmit_CreateDefault(NotDefaultableEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id.Value}: {d.Message}");
            return false.ToProperty()
                .Label($"Compilation failed for NotDefaultable scenario.\nErrors: {string.Join("; ", errors)}\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# content found.\nSource:\n{scenario.Source}");
        }

        var hasCreateDefault = HasCreateDefaultForType(emittedCSharp, scenario.TypeName);

        return (!hasCreateDefault).ToProperty()
            .Label($"NotDefaultable type '{scenario.TypeName}' should NOT have __CreateDefault() but it was found.\nEmitted C#:\n{emittedCSharp}");
    }

    /// <summary>
    /// Property 10b: Types with no constructor and all defaultable fields (Inferred)
    /// SHALL have __CreateDefault() in the emitted C#.
    ///
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(CreateDefaultEmissionPropertyTests) })]
    public Property InferredDefaultableType_Emits_CreateDefault(InferredDefaultableEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id.Value}: {d.Message}");
            return false.ToProperty()
                .Label($"Compilation failed for Inferred scenario.\nErrors: {string.Join("; ", errors)}\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# content found.\nSource:\n{scenario.Source}");
        }

        var hasCreateDefault = HasCreateDefaultForType(emittedCSharp, scenario.TypeName);

        return hasCreateDefault.ToProperty()
            .Label($"Inferred defaultable type '{scenario.TypeName}' should have __CreateDefault() but it was NOT found.\nEmitted C#:\n{emittedCSharp}");
    }

    /// <summary>
    /// Property 10c: Types with an explicit default block (Explicit)
    /// SHALL have __CreateDefault() in the emitted C#.
    ///
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(CreateDefaultEmissionPropertyTests) })]
    public Property ExplicitDefaultableType_Emits_CreateDefault(ExplicitDefaultableEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id.Value}: {d.Message}");
            return false.ToProperty()
                .Label($"Compilation failed for Explicit scenario.\nErrors: {string.Join("; ", errors)}\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# content found.\nSource:\n{scenario.Source}");
        }

        var hasCreateDefault = HasCreateDefaultForType(emittedCSharp, scenario.TypeName);

        return hasCreateDefault.ToProperty()
            .Label($"Explicit defaultable type '{scenario.TypeName}' should have __CreateDefault() but it was NOT found.\nEmitted C#:\n{emittedCSharp}");
    }
}