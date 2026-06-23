// Feature: abstract-type-unification, Property 6: Slot signature matching is exact
//
// For any abstract type slot and any concrete type implementation where the name
// matches but either the return type or at least one parameter type differs, the
// type checker SHALL reject the implementation as not satisfying the slot.
//
// Uses in-process compilation via CompilerDriver — zero process spawns.
//
// **Validates: Requirements 4.6**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

/// <summary>
/// Property-based tests verifying that near-miss signatures (wrong return type or
/// wrong parameter type) are rejected as not satisfying the slot.
///
/// Property 6: Slot signature matching is exact — for any abstract type slot and
/// any concrete type implementation where the name matches but either the return
/// type or at least one parameter type differs, the type checker SHALL reject
/// the implementation as not satisfying the slot.
/// </summary>
public sealed class ExactSignatureMatchPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public ExactSignatureMatchPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistExactSigPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across all iterations) ────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario where a concrete type implements an abstract type
    /// but provides a near-miss signature for one slot (wrong return type or wrong
    /// parameter type), while correctly implementing all other slots.
    /// </summary>
#pragma warning disable CA1034
    public record NearMissScenario(
        string AbstractName,
        string ClassName,
        string SlotReturnType,
        string SlotName,
        ImmutableArray<(string Type, string ParamName)> SlotParameters,
        NearMissKind MissKind,
        string WrongType)
    {
        public override string ToString()
        {
            var parms = string.Join(", ", SlotParameters.Select(p => $"{p.Type} {p.ParamName}"));
            var slotSig = $"{SlotReturnType} {SlotName}({parms})";
            return MissKind switch
            {
                NearMissKind.WrongReturnType =>
                    $"abstract {AbstractName} {{ {slotSig}; }} class {ClassName} returns {WrongType} instead of {SlotReturnType}",
                NearMissKind.WrongParameterType =>
                    $"abstract {AbstractName} {{ {slotSig}; }} class {ClassName} has param type {WrongType} instead of {SlotParameters[0].Type}",
                _ => "unknown"
            };
        }
    }

    public enum NearMissKind
    {
        WrongReturnType,
        WrongParameterType
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNames =
        ["IFoo", "IBar", "IBaz", "IProcessor", "IHandler"];

    private static readonly string[] ClassNames =
        ["MyClass", "Handler", "Widget", "Service", "Impl"];

    private static readonly string[] MethodNames =
        ["Execute", "Process", "Compute", "Transform", "Validate"];

    private static readonly string[] Types = ["string", "int", "bool"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<NearMissScenario> GenNearMissScenario()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from className in Gen.Elements(ClassNames)
               from slotName in Gen.Elements(MethodNames)
               from slotReturnType in Gen.Elements(Types)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(Types)
               from missKind in Gen.Elements(NearMissKind.WrongReturnType, NearMissKind.WrongParameterType)
               from wrongType in Gen.Elements(Types)
               let slotParams = hasParam
                   ? ImmutableArray.Create<(string, string)>((paramType, "x"))
                   : ImmutableArray<(string, string)>.Empty
               // Ensure near-miss is actually different
               let effectiveMissKind = missKind == NearMissKind.WrongParameterType && !hasParam
                   ? NearMissKind.WrongReturnType  // fall back if no param to mismatch
                   : missKind
               let effectiveWrongType = effectiveMissKind switch
               {
                   NearMissKind.WrongReturnType => wrongType == slotReturnType
                       ? Types.First(t => t != slotReturnType)
                       : wrongType,
                   NearMissKind.WrongParameterType => wrongType == paramType
                       ? Types.First(t => t != paramType)
                       : wrongType,
                   _ => wrongType
               }
               select new NearMissScenario(
                   abstractName,
                   className,
                   slotReturnType,
                   slotName,
                   slotParams,
                   effectiveMissKind,
                   effectiveWrongType);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<NearMissScenario> ArbitraryNearMissScenario() =>
        GenNearMissScenario().ToArbitrary();

    // ─── Source generation ───────────────────────────────────────────────

    private static string GenerateSource(NearMissScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace TestProject;");
        sb.AppendLine();

        // Emit abstract type with a single slot
        var slotParms = string.Join(", ", scenario.SlotParameters.Select(p => $"{p.Type} {p.ParamName}"));
        sb.AppendLine($"abstract {scenario.AbstractName}");
        sb.AppendLine("{");
        sb.AppendLine($"    {scenario.SlotReturnType} {scenario.SlotName}({slotParms});");
        sb.AppendLine("}");
        sb.AppendLine();

        // Emit concrete class with near-miss implementation
        sb.AppendLine($"public class {scenario.ClassName} : {scenario.AbstractName}");
        sb.AppendLine("{");

        string implReturnType;
        string implParms;

        switch (scenario.MissKind)
        {
            case NearMissKind.WrongReturnType:
                implReturnType = scenario.WrongType;
                implParms = slotParms;
                break;
            case NearMissKind.WrongParameterType:
                implReturnType = scenario.SlotReturnType;
                // Replace the first parameter's type with the wrong type
                var modifiedParams = scenario.SlotParameters
                    .Select((p, i) => i == 0 ? (scenario.WrongType, p.ParamName) : p)
                    .ToImmutableArray();
                implParms = string.Join(", ", modifiedParams.Select(p => $"{p.Item1} {p.ParamName}"));
                break;
            default:
                implReturnType = scenario.SlotReturnType;
                implParms = slotParms;
                break;
        }

        var body = GetDefaultReturnBody(implReturnType);
        sb.AppendLine($"    public {implReturnType} {scenario.SlotName}({implParms})");
        sb.AppendLine("    {");
        sb.AppendLine($"        {body}");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Entry point (required for compilation)
        sb.AppendLine("public class Program");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Main(string[] args)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetDefaultReturnBody(string returnType) => returnType switch
    {
        "string" => "return \"hello\";",
        "int" => "return 42;",
        "bool" => "return true;",
        _ => "return 0;",
    };

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Slot signature matching is exact
    //
    // **Validates: Requirements 4.6**
    //
    // For any abstract type slot and any concrete type implementation where
    // the name matches but either the return type or at least one parameter
    // type differs, the type checker SHALL reject the implementation as not
    // satisfying the slot (producing MIST_SM_0022 "does not implement
    // abstract member").
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ExactSignatureMatchPropertyTests) })]
    public Property NearMissSignature_ProducesUnfilledSlotError(NearMissScenario scenario)
    {
        var source = GenerateSource(scenario);
        var result = CompileSource(source);

        // The near-miss implementation should NOT satisfy the slot,
        // so the compiler should emit a "does not implement abstract member" error
        var unfilledSlotDiags = result.Diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022" &&
                        d.Message.Contains("does not implement abstract member"))
            .ToList();

        if (unfilledSlotDiags.Count == 0)
        {
            return false.ToProperty()
                .Label($"Expected MIST_SM_0022 'does not implement abstract member' diagnostic but got none.\n" +
                       $"Miss kind: {scenario.MissKind}, Wrong type: {scenario.WrongType}\n" +
                       $"All diagnostics: [{string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{source}");
        }

        // The diagnostic should mention the slot name
        var mentionsSlot = unfilledSlotDiags.Any(d => d.Message.Contains(scenario.SlotName));
        if (!mentionsSlot)
        {
            return false.ToProperty()
                .Label($"MIST_SM_0022 diagnostic found but does not mention slot name '{scenario.SlotName}'.\n" +
                       $"Diagnostics: [{string.Join("; ", unfilledSlotDiags.Select(d => d.Message))}]\n" +
                       $"Source:\n{source}");
        }

        return true.ToProperty();
    }
}
