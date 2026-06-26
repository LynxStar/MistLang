// Feature: deep-ownership-types, Property 6: Move semantics — source becomes dead

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for deep ownership move semantics (Property 6).
///
/// Property 6: Move Semantics — Source Becomes Dead
/// For any owned binding that is moved (assigned to another owned binding, assigned to
/// an unrestricted binding, or passed to an 'owned' parameter), the source binding SHALL
/// transition to the Moved state, and any subsequent read, write, lend, or move of the
/// source SHALL produce a use-after-move error.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 12.4**
/// </summary>
public class DeepOwnershipMovePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario where an owned binding is moved to another owned binding,
    /// then the source is accessed (use-after-move expected).
    /// </summary>
    public sealed record OwnedToOwnedMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned binding is moved to an unrestricted binding,
    /// then the source is accessed (use-after-move expected).
    /// </summary>
    public sealed record OwnedToUnrestrictedMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned binding is passed to an 'owned' parameter,
    /// then the source is accessed (use-after-move expected).
    /// </summary>
    public sealed record OwnedParameterMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned binding is moved but never accessed afterwards
    /// (valid — no diagnostic expected).
    /// </summary>
    public sealed record ValidMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Resource", "Buffer", "Handle", "Connection", "Stream", "Channel"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "item", "res"];

    private static readonly string[] DestVarNames =
        ["dest", "target", "other", "sink", "receiver", "output"];

    private static readonly string[] MethodNames =
        ["Consume", "Transfer", "Accept", "Process", "Absorb", "Ingest"];

    /// <summary>
    /// Access kinds that can be attempted on a moved variable.
    /// Each produces a different code snippet accessing the source after move.
    /// </summary>
    private enum PostMoveAccess
    {
        Read,
        Write,
        MethodCall,
        PassAsArg,
    }

    private static readonly PostMoveAccess[] AllPostMoveAccesses =
        [PostMoveAccess.Read, PostMoveAccess.Write, PostMoveAccess.MethodCall, PostMoveAccess.PassAsArg];

    // ─── Source code generation helpers ──────────────────────────────────

    /// <summary>
    /// Returns a code snippet that accesses the given variable in the specified way.
    /// </summary>
    private static string GetAccessCode(string varName, PostMoveAccess access, string methodName) => access switch
    {
        PostMoveAccess.Read => $"int temp = {varName}.value;",
        PostMoveAccess.Write => $"{varName}.value = 99;",
        PostMoveAccess.MethodCall => $"{varName}.GetValue();",
        PostMoveAccess.PassAsArg => $"{methodName}({varName});",
        _ => throw new ArgumentOutOfRangeException(nameof(access)),
    };

    /// <summary>
    /// Builds a program where an owned binding is moved to another owned binding,
    /// then the source is accessed.
    /// </summary>
    private static string BuildOwnedToOwnedMoveProgram(
        string className, string varName, string destName,
        string accessCode, PostMoveAccess access, string methodName)
    {
        var consumeMethod = access == PostMoveAccess.PassAsArg
            ? $"    public static void {methodName}(owned {className} param) {{\n    }}\n"
            : "";

        return
            $"owned class {className} {{\n" +
            $"    public int value;\n" +
            $"    public int GetValue() {{\n" +
            $"        return value;\n" +
            $"    }}\n" +
            $"}}\n" +
            $"class Program {{\n" +
            consumeMethod +
            $"    public static void Main() {{\n" +
            $"        owned {className} {varName} = new {className}();\n" +
            $"        owned {className} {destName} = {varName};\n" +
            $"        {accessCode}\n" +
            $"    }}\n" +
            $"}}";
    }

    /// <summary>
    /// Builds a program where an owned binding is moved to an unrestricted binding,
    /// then the source is accessed.
    /// </summary>
    private static string BuildOwnedToUnrestrictedMoveProgram(
        string className, string varName, string destName,
        string accessCode, PostMoveAccess access, string methodName)
    {
        var consumeMethod = access == PostMoveAccess.PassAsArg
            ? $"    public static void {methodName}(owned {className} param) {{\n    }}\n"
            : "";

        return
            $"owned class {className} {{\n" +
            $"    public int value;\n" +
            $"    public int GetValue() {{\n" +
            $"        return value;\n" +
            $"    }}\n" +
            $"}}\n" +
            $"class Program {{\n" +
            consumeMethod +
            $"    public static void Main() {{\n" +
            $"        owned {className} {varName} = new {className}();\n" +
            $"        {className} {destName} = {varName};\n" +
            $"        {accessCode}\n" +
            $"    }}\n" +
            $"}}";
    }

    /// <summary>
    /// Builds a program where an owned binding is passed to an 'owned' parameter,
    /// then the source is accessed.
    /// </summary>
    private static string BuildOwnedParameterMoveProgram(
        string className, string varName, string methodName,
        string accessCode, PostMoveAccess access, string secondMethodName)
    {
        var secondConsumeMethod = access == PostMoveAccess.PassAsArg
            ? $"    public static void {secondMethodName}(owned {className} param) {{\n    }}\n"
            : "";

        return
            $"owned class {className} {{\n" +
            $"    public int value;\n" +
            $"    public int GetValue() {{\n" +
            $"        return value;\n" +
            $"    }}\n" +
            $"}}\n" +
            $"class Program {{\n" +
            $"    public static void {methodName}(owned {className} param) {{\n" +
            $"    }}\n" +
            secondConsumeMethod +
            $"    public static void Main() {{\n" +
            $"        owned {className} {varName} = new {className}();\n" +
            $"        {methodName}({varName});\n" +
            $"        {accessCode}\n" +
            $"    }}\n" +
            $"}}";
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates programs where an owned binding is moved to another owned binding,
    /// then the source is accessed in various ways.
    /// Validates: Requirement 5.1
    /// </summary>
    private static Gen<OwnedToOwnedMoveScenario> GenOwnedToOwnedMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               from access in Gen.Elements(AllPostMoveAccesses)
               from methodName in Gen.Elements(MethodNames)
               let accessCode = GetAccessCode(varName, access, methodName)
               let source = BuildOwnedToOwnedMoveProgram(className, varName, destName, accessCode, access, methodName)
               select new OwnedToOwnedMoveScenario(source,
                   $"owned->owned: move {varName}->{destName}, then {access}");
    }

    /// <summary>
    /// Generates programs where an owned binding is moved to an unrestricted binding,
    /// then the source is accessed in various ways.
    /// Validates: Requirement 5.2
    /// </summary>
    private static Gen<OwnedToUnrestrictedMoveScenario> GenOwnedToUnrestrictedMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               from access in Gen.Elements(AllPostMoveAccesses)
               from methodName in Gen.Elements(MethodNames)
               let accessCode = GetAccessCode(varName, access, methodName)
               let source = BuildOwnedToUnrestrictedMoveProgram(className, varName, destName, accessCode, access, methodName)
               select new OwnedToUnrestrictedMoveScenario(source,
                   $"owned->unrestricted: move {varName}->{destName}, then {access}");
    }

    /// <summary>
    /// Generates programs where an owned binding is passed to an 'owned' parameter,
    /// then the source is accessed in various ways.
    /// Validates: Requirements 5.3, 12.4
    /// </summary>
    private static Gen<OwnedParameterMoveScenario> GenOwnedParameterMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               from access in Gen.Elements(AllPostMoveAccesses)
               let secondMethodName = methodName + "2"
               let accessCode = GetAccessCode(varName, access,
                   access == PostMoveAccess.PassAsArg ? secondMethodName : methodName)
               let source = BuildOwnedParameterMoveProgram(className, varName, methodName, accessCode, access, secondMethodName)
               select new OwnedParameterMoveScenario(source,
                   $"owned->param: pass {varName} to {methodName}(owned), then {access}");
    }

    /// <summary>
    /// Generates programs where an owned binding is moved but never accessed afterwards.
    /// These should produce no use-after-move errors.
    /// Validates: Requirements 5.1, 5.2, 5.3
    /// </summary>
    private static Gen<ValidMoveScenario> GenValidMove()
    {
        return Gen.OneOf(
            GenValidOwnedToOwnedMove(),
            GenValidOwnedToUnrestrictedMove(),
            GenValidOwnedToParameterMove());
    }

    private static Gen<ValidMoveScenario> GenValidOwnedToOwnedMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"    public int value;\n" +
                   $"    public int GetValue() {{\n" +
                   $"        return value;\n" +
                   $"    }}\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        owned {className} {destName} = {varName};\n" +
                   $"        {destName}.GetValue();\n" +
                   $"    }}\n" +
                   $"}}"
               select new ValidMoveScenario(source,
                   $"valid owned->owned: move {varName}->{destName}, access only {destName}");
    }

    private static Gen<ValidMoveScenario> GenValidOwnedToUnrestrictedMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"    public int value;\n" +
                   $"    public int GetValue() {{\n" +
                   $"        return value;\n" +
                   $"    }}\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        {className} {destName} = {varName};\n" +
                   $"        {destName}.GetValue();\n" +
                   $"    }}\n" +
                   $"}}"
               select new ValidMoveScenario(source,
                   $"valid owned->unrestricted: move {varName}->{destName}, access only {destName}");
    }

    private static Gen<ValidMoveScenario> GenValidOwnedToParameterMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"    public int value;\n" +
                   $"    public int GetValue() {{\n" +
                   $"        return value;\n" +
                   $"    }}\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void {methodName}(owned {className} param) {{\n" +
                   $"    }}\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        {methodName}({varName});\n" +
                   $"    }}\n" +
                   $"}}"
               select new ValidMoveScenario(source,
                   $"valid owned->param: pass {varName} to {methodName}(owned), no access after");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<OwnedToOwnedMoveScenario> ArbitraryOwnedToOwnedMoveScenario() =>
        GenOwnedToOwnedMove().ToArbitrary();

    public static Arbitrary<OwnedToUnrestrictedMoveScenario> ArbitraryOwnedToUnrestrictedMoveScenario() =>
        GenOwnedToUnrestrictedMove().ToArbitrary();

    public static Arbitrary<OwnedParameterMoveScenario> ArbitraryOwnedParameterMoveScenario() =>
        GenOwnedParameterMove().ToArbitrary();

    public static Arbitrary<ValidMoveScenario> ArbitraryValidMoveScenario() =>
        GenValidMove().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6a: Owned-to-owned move — source becomes dead
    //
    // When an owned binding is assigned to another owned binding, the source
    // transitions to Moved state. Any subsequent access produces
    // MIST_OWN_USE_AFTER_MOVE.
    //
    // **Validates: Requirement 5.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipMovePropertyTests) })]
    public Property OwnedToOwnedMove_SourceBecomesDead(OwnedToOwnedMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasUseAfterMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");

        return hasUseAfterMove.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_USE_AFTER_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6b: Owned-to-unrestricted move — source becomes dead
    //
    // When an owned binding is assigned to an unrestricted binding, the source
    // transitions to Moved state. Any subsequent access produces
    // MIST_OWN_USE_AFTER_MOVE.
    //
    // **Validates: Requirement 5.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipMovePropertyTests) })]
    public Property OwnedToUnrestrictedMove_SourceBecomesDead(OwnedToUnrestrictedMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasUseAfterMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");

        return hasUseAfterMove.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_USE_AFTER_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6c: Owned-to-parameter move — source becomes dead
    //
    // When an owned binding is passed to an 'owned' parameter, the source
    // transitions to Moved state. Any subsequent access produces
    // MIST_OWN_USE_AFTER_MOVE.
    //
    // **Validates: Requirements 5.3, 12.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipMovePropertyTests) })]
    public Property OwnedToParameterMove_SourceBecomesDead(OwnedParameterMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasUseAfterMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");

        return hasUseAfterMove.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_USE_AFTER_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6d: Valid moves — no use-after-move error
    //
    // When an owned binding is moved but never accessed afterwards, no
    // use-after-move diagnostic should be reported.
    //
    // **Validates: Requirements 5.1, 5.2, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipMovePropertyTests) })]
    public Property ValidMove_NoUseAfterMoveError(ValidMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasUseAfterMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");

        return (!hasUseAfterMove).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected no MIST_OWN_USE_AFTER_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}