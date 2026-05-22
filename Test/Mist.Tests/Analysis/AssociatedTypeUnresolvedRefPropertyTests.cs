// Feature: associated-types, Property 10: Invalid associated type reference emits diagnostic
// **Validates: Requirements 5.4, 8.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that the compiler emits diagnostic MIST_AT_0004
/// when a generic type parameter's dot-accessed name is not an associated type slot
/// of any constraining interface or trait.
///
/// <b>Property 10</b>: For any generic type parameter <c>T</c> constrained by an interface,
/// and any identifier <c>X</c> that is not an associated type slot of any constraining
/// interface or trait, a reference to <c>T.X</c> in a type position SHALL cause the type
/// checker to emit diagnostic <c>MIST_AT_0004</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AssociatedTypeUnresolvedRefPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(lexDiag.ToImmutableArray());
        all.AddRange(parseDiag.ToImmutableArray());
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNames =
        ["IContainer", "IFactory", "IRepository", "IMapper", "IProvider"];

    private static readonly string[] ValidSlotNames =
        ["Element", "Product", "Key", "Value", "Iterator"];

    private static readonly string[] InvalidSlotNames =
        ["Foo", "Bar", "Widget", "Unknown", "Missing", "Bogus", "Nope", "Wrong"];

    private static readonly string[] TypeParamNames =
        ["T", "U", "V", "TItem", "TSource"];

    // ─── Type position kinds ─────────────────────────────────────────────

    /// <summary>Where the invalid T.X reference appears in the source.</summary>
    public enum TypePosition
    {
        /// <summary>T.X used as a parameter type.</summary>
        Parameter,
        /// <summary>T.X used as a return type.</summary>
        ReturnType,
        /// <summary>T.X used as a local variable type.</summary>
        LocalVariable
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>Describes a scenario where T.X references a non-existent slot.</summary>
    public sealed class UnresolvedRefScenario
    {
        public string InterfaceName { get; }
        public IReadOnlyList<string> DeclaredSlots { get; }
        public string TypeParamName { get; }
        public string InvalidSlotName { get; }
        public TypePosition Position { get; }
        public bool HasConstraint { get; }

        public UnresolvedRefScenario(
            string interfaceName,
            IReadOnlyList<string> declaredSlots,
            string typeParamName,
            string invalidSlotName,
            TypePosition position,
            bool hasConstraint)
        {
            InterfaceName = interfaceName;
            DeclaredSlots = declaredSlots;
            TypeParamName = typeParamName;
            InvalidSlotName = invalidSlotName;
            Position = position;
            HasConstraint = hasConstraint;
        }

        public override string ToString()
        {
            var constraint = HasConstraint ? $" where {TypeParamName} : {InterfaceName}" : " (unconstrained)";
            return $"{TypeParamName}.{InvalidSlotName} in {Position}{constraint} " +
                   $"[declared slots: {string.Join(", ", DeclaredSlots)}]";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<UnresolvedRefScenario> GenScenario()
    {
        return from ifaceName in Gen.Elements(InterfaceNames)
               from slotCount in Gen.Choose(1, 3)
               from slotIndices in Gen.ArrayOf(Gen.Choose(0, ValidSlotNames.Length - 1), slotCount)
               let declaredSlots = slotIndices.Select(i => ValidSlotNames[i]).Distinct().ToArray()
               where declaredSlots.Length > 0
               from invalidSlot in Gen.Elements(InvalidSlotNames)
               where !declaredSlots.Contains(invalidSlot)
               from typeParam in Gen.Elements(TypeParamNames)
               from position in Gen.Elements(TypePosition.Parameter, TypePosition.ReturnType, TypePosition.LocalVariable)
               from hasConstraint in Gen.Elements(true, false)
               select new UnresolvedRefScenario(
                   ifaceName, declaredSlots, typeParam, invalidSlot, position, hasConstraint);
    }

    // ─── Source generation ───────────────────────────────────────────────

    private static string BuildSource(UnresolvedRefScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        if (scenario.HasConstraint)
        {
            // Emit the interface with declared slots
            sb.AppendLine($"interface {scenario.InterfaceName} {{");
            foreach (var slot in scenario.DeclaredSlots)
            {
                sb.AppendLine($"    type {slot};");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Emit a class with a generic method that references T.InvalidSlot
        sb.AppendLine("class TestClass {");

        var whereClause = scenario.HasConstraint
            ? $" where {scenario.TypeParamName} : {scenario.InterfaceName}"
            : "";

        switch (scenario.Position)
        {
            case TypePosition.Parameter:
                sb.AppendLine($"    void Process<{scenario.TypeParamName}>({scenario.TypeParamName}.{scenario.InvalidSlotName} item){whereClause} {{");
                sb.AppendLine("    }");
                break;

            case TypePosition.ReturnType:
                sb.AppendLine($"    {scenario.TypeParamName}.{scenario.InvalidSlotName} Create<{scenario.TypeParamName}>(){whereClause} {{");
                sb.AppendLine("    }");
                break;

            case TypePosition.LocalVariable:
                sb.AppendLine($"    void Use<{scenario.TypeParamName}>({scenario.TypeParamName} value){whereClause} {{");
                sb.AppendLine($"        {scenario.TypeParamName}.{scenario.InvalidSlotName} x = value;");
                sb.AppendLine("    }");
                break;
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<UnresolvedRefScenario> ArbitraryUnresolvedRefScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Invalid associated type reference emits diagnostic
    //
    // **Validates: Requirements 5.4, 8.3**
    //
    // For any generic type parameter T constrained by an interface, and any
    // identifier X that is not an associated type slot of any constraining
    // interface or trait, a reference to T.X in a type position SHALL cause
    // the type checker to emit diagnostic MIST_AT_0004.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeUnresolvedRefPropertyTests) })]
    public Property InvalidAssociatedTypeReference_EmitsMIST_AT_0004(UnresolvedRefScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();

        if (at0004.Count == 0)
            return false.ToProperty()
                .Label($"Expected MIST_AT_0004 but none emitted.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        // Verify the diagnostic message mentions the invalid slot name
        var mentionsSlot = at0004.Any(d => d.Message.Contains(scenario.InvalidSlotName));
        if (!mentionsSlot)
            return false.ToProperty()
                .Label($"MIST_AT_0004 emitted but does not mention '{scenario.InvalidSlotName}'.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Messages: [{string.Join(", ", at0004.Select(d => d.Message))}]");

        // Verify the diagnostic message mentions the type parameter name
        var mentionsTypeParam = at0004.Any(d => d.Message.Contains(scenario.TypeParamName));
        if (!mentionsTypeParam)
            return false.ToProperty()
                .Label($"MIST_AT_0004 emitted but does not mention '{scenario.TypeParamName}'.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Messages: [{string.Join(", ", at0004.Select(d => d.Message))}]");

        return true.ToProperty();
    }
}
