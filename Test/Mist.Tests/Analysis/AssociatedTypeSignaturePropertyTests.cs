// Feature: associated-types, Property 8: Implementor signature substitution
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that the type checker correctly validates
/// implementor method signatures against interface methods that reference
/// associated type slot names.
///
/// <list type="bullet">
///   <item><b>Property 8</b>: For any interface method whose signature references an
///   associated type slot name (as return type, parameter type, or generic type argument),
///   and any implementor of that interface, the type checker SHALL accept the implementor's
///   method only if its signature uses the corresponding concrete type member in every
///   position where the interface signature uses the slot name. Using any other type in
///   those positions SHALL produce a diagnostic error.</item>
/// </list>
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public sealed class AssociatedTypeSignaturePropertyTests : IDisposable
{
    private readonly string _outputDir;

    public AssociatedTypeSignaturePropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistAssocTypeSig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private ImmutableArray<Diagnostic> CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

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

        var result = driver.Compile(options);
        return result.Diagnostics;
    }

    // ─── Shared name pools ───────────────────────────────────────────────

    private static readonly string[] SlotNames = ["Output", "Element", "Result", "Item", "Node"];
    private static readonly string[] InterfaceNames = ["IProducer", "IContainer", "IProcessor", "IFactory"];
    private static readonly string[] MethodNames = ["Create", "Process", "Transform", "Get", "Make"];
    private static readonly string[] ClassNames = ["MyProducer", "MyContainer", "MyProcessor", "MyFactory"];

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>Describes where the associated type slot is used in the method signature.</summary>
    public enum SlotPosition
    {
        ReturnType,
        ParameterType,
        Both
    }

#pragma warning disable CA1034

    /// <summary>Describes a correct substitution scenario (should compile without signature errors).</summary>
    public record CorrectSubstitutionScenario(
        string InterfaceName,
        string SlotName,
        string MethodName,
        string ClassName,
        SlotPosition Position,
        string Description)
    {
        public override string ToString() => Description;
    }

    /// <summary>Describes an incorrect substitution scenario (should produce a diagnostic).</summary>
    public record IncorrectSubstitutionScenario(
        string InterfaceName,
        string SlotName,
        string MethodName,
        string ClassName,
        SlotPosition Position,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<CorrectSubstitutionScenario> GenCorrectSubstitutionScenario()
    {
        return from interfaceName in Gen.Elements(InterfaceNames)
               from slotName in Gen.Elements(SlotNames)
               from methodName in Gen.Elements(MethodNames)
               from className in Gen.Elements(ClassNames)
               from position in Gen.Elements(SlotPosition.ReturnType, SlotPosition.ParameterType, SlotPosition.Both)
               let desc = $"{className} : {interfaceName} (slot={slotName}, method={methodName}, pos={position})"
               select new CorrectSubstitutionScenario(interfaceName, slotName, methodName, className, position, desc);
    }

    private static Gen<IncorrectSubstitutionScenario> GenIncorrectSubstitutionScenario()
    {
        return from interfaceName in Gen.Elements(InterfaceNames)
               from slotName in Gen.Elements(SlotNames)
               from methodName in Gen.Elements(MethodNames)
               from className in Gen.Elements(ClassNames)
               from position in Gen.Elements(SlotPosition.ReturnType, SlotPosition.ParameterType, SlotPosition.Both)
               let desc = $"{className} : {interfaceName} (slot={slotName}, method={methodName}, pos={position}, wrong=int)"
               select new IncorrectSubstitutionScenario(interfaceName, slotName, methodName, className, position, desc);
    }

    public static Arbitrary<CorrectSubstitutionScenario> ArbitraryCorrectSubstitutionScenario() =>
        GenCorrectSubstitutionScenario().ToArbitrary();

    public static Arbitrary<IncorrectSubstitutionScenario> ArbitraryIncorrectSubstitutionScenario() =>
        GenIncorrectSubstitutionScenario().ToArbitrary();

    // ─── Source generation helpers ───────────────────────────────────────

    /// <summary>
    /// Builds a correct source where the implementor provides a nested class
    /// with the same name as the slot and uses it in the method signature.
    /// </summary>
    private static string BuildCorrectSource(CorrectSubstitutionScenario scenario)
    {
        var interfaceMethod = scenario.Position switch
        {
            SlotPosition.ReturnType => $"    {scenario.SlotName} {scenario.MethodName}();",
            SlotPosition.ParameterType => $"    void {scenario.MethodName}({scenario.SlotName} input);",
            SlotPosition.Both => $"    {scenario.SlotName} {scenario.MethodName}({scenario.SlotName} input);",
            _ => throw new ArgumentOutOfRangeException()
        };

        // The implementor provides a nested class with the same name as the slot.
        // The method signature uses the slot name (which resolves to the nested class).
        var implMethod = scenario.Position switch
        {
            SlotPosition.ReturnType => $"    public {scenario.SlotName} {scenario.MethodName}() {{ return new {scenario.SlotName}(); }}",
            SlotPosition.ParameterType => $"    public void {scenario.MethodName}({scenario.SlotName} input) {{ }}",
            SlotPosition.Both => $"    public {scenario.SlotName} {scenario.MethodName}({scenario.SlotName} input) {{ return input; }}",
            _ => throw new ArgumentOutOfRangeException()
        };

        return $@"
abstract {scenario.InterfaceName} {{
    type {scenario.SlotName};
{interfaceMethod}
}}

class {scenario.ClassName} : {scenario.InterfaceName} {{
    public class {scenario.SlotName} {{
    }}
{implMethod}
}}

class Program {{
    public static void Main() {{
    }}
}}
";
    }

    /// <summary>
    /// Builds an incorrect source where the implementor provides a nested class
    /// with the correct name but uses a different type (int) in the method signature.
    /// </summary>
    private static string BuildIncorrectSource(IncorrectSubstitutionScenario scenario)
    {
        var interfaceMethod = scenario.Position switch
        {
            SlotPosition.ReturnType => $"    {scenario.SlotName} {scenario.MethodName}();",
            SlotPosition.ParameterType => $"    void {scenario.MethodName}({scenario.SlotName} input);",
            SlotPosition.Both => $"    {scenario.SlotName} {scenario.MethodName}({scenario.SlotName} input);",
            _ => throw new ArgumentOutOfRangeException()
        };

        // The implementor provides the nested class but uses 'int' in the method
        // signature instead of the slot name — this is a signature mismatch.
        var implMethod = scenario.Position switch
        {
            SlotPosition.ReturnType => $"    public int {scenario.MethodName}() {{ return 0; }}",
            SlotPosition.ParameterType => $"    public void {scenario.MethodName}(int input) {{ }}",
            SlotPosition.Both => $"    public int {scenario.MethodName}(int input) {{ return 0; }}",
            _ => throw new ArgumentOutOfRangeException()
        };

        return $@"
abstract {scenario.InterfaceName} {{
    type {scenario.SlotName};
{interfaceMethod}
}}

class {scenario.ClassName} : {scenario.InterfaceName} {{
    public class {scenario.SlotName} {{
    }}
{implMethod}
}}

class Program {{
    public static void Main() {{
    }}
}}
";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8a: Correct substitution — accepted without diagnostic
    //
    // **Validates: Requirements 4.1, 4.2, 4.3**
    //
    // For any interface method whose signature references an associated type
    // slot name, and any implementor that uses the corresponding concrete
    // type member in every position, the type checker SHALL accept the
    // implementor's method without emitting MIST_SM_0022.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeSignaturePropertyTests) })]
    public Property CorrectSubstitution_IsAccepted(CorrectSubstitutionScenario scenario)
    {
        var source = BuildCorrectSource(scenario);
        var diagnostics = CompileSource(source);

        // Filter for interface satisfaction diagnostics that mention the method name
        var signatureMismatch = diagnostics.Where(d =>
            d.Id.Value == "MIST_SM_0022" &&
            d.Message.Contains(scenario.MethodName)).ToList();

        if (signatureMismatch.Count > 0)
            return false.ToProperty()
                .Label($"Expected no MIST_SM_0022 for method '{scenario.MethodName}' but got {signatureMismatch.Count}.\n" +
                       $"Source:\n{source}\n" +
                       $"Diagnostics: [{string.Join(", ", signatureMismatch.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8b: Incorrect substitution — diagnostic emitted
    //
    // **Validates: Requirements 4.3, 4.4**
    //
    // For any interface method whose signature references an associated type
    // slot name, and any implementor that uses a different type (not the
    // concrete type member) in those positions, the type checker SHALL emit
    // a diagnostic error (MIST_SM_0022).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeSignaturePropertyTests) })]
    public Property IncorrectSubstitution_EmitsDiagnostic(IncorrectSubstitutionScenario scenario)
    {
        var source = BuildIncorrectSource(scenario);
        var diagnostics = CompileSource(source);

        // The type checker should emit MIST_SM_0022 because the implementor's
        // method signature uses the wrong type where the interface uses the slot name.
        var signatureMismatch = diagnostics.Where(d =>
            d.Id.Value == "MIST_SM_0022" &&
            d.Message.Contains(scenario.MethodName)).ToList();

        if (signatureMismatch.Count == 0)
            return false.ToProperty()
                .Label($"Expected MIST_SM_0022 for method '{scenario.MethodName}' but none emitted.\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        return true.ToProperty();
    }
}
