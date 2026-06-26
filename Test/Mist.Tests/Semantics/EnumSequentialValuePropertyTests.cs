// Feature: enums, Property 4: Sequential value assignment
// **Validates: Requirements 4.1, 4.2, 4.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Describes a generated enum scenario for sequential value assignment testing.
/// Each member is either name-only (implicit value) or has an explicit integer value.
/// </summary>
public sealed class EnumSequentialValueScenario
{
    public string EnumName { get; }
    public IReadOnlyList<(string Name, long? ExplicitValue)> Members { get; }

    /// <summary>
    /// The expected resolved values for each member, computed from the sequencing rules.
    /// </summary>
    public IReadOnlyList<long> ExpectedValues { get; }

    public EnumSequentialValueScenario(
        string enumName,
        IReadOnlyList<(string Name, long? ExplicitValue)> members,
        IReadOnlyList<long> expectedValues)
    {
        EnumName = enumName;
        Members = members;
        ExpectedValues = expectedValues;
    }

    public override string ToString()
    {
        var memberDescs = Members.Select(m =>
            m.ExplicitValue.HasValue ? $"{m.Name}:{m.ExplicitValue}" : m.Name);
        return $"enum {EnumName} {{ {string.Join(", ", memberDescs)} }} → [{string.Join(", ", ExpectedValues)}]";
    }
}

/// <summary>
/// Property-based tests for sequential value assignment.
///
/// **Validates: Requirements 4.1, 4.2, 4.3**
///
/// For any enum with N members where some or all members lack explicit values,
/// each implicit member SHALL receive a value equal to the previous member's value
/// plus 1 (with the first implicit member receiving 0 if it is the first member
/// overall, or previous+1 otherwise).
/// </summary>
public class EnumSequentialValuePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Rank", "Priority", "Level", "Phase", "Step", "Grade", "Tier"];

    private static readonly string[] MemberNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta",
         "Theta", "Iota", "Kappa", "Lambda", "Mu"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Computes expected values from a member list using the sequencing rules:
    /// - First implicit member gets 0 (Req 4.1)
    /// - Subsequent implicit members get previous + 1 (Req 4.2)
    /// - Implicit member after explicit gets explicit + 1 (Req 4.3)
    /// </summary>
    private static IReadOnlyList<long> ComputeExpectedValues(
        IReadOnlyList<(string Name, long? ExplicitValue)> members)
    {
        var expected = new List<long>();
        long nextValue = 0;

        foreach(var member in members)
        {
            if(member.ExplicitValue.HasValue)
            {
                expected.Add(member.ExplicitValue.Value);
                nextValue = member.ExplicitValue.Value + 1;
            }
            else
            {
                expected.Add(nextValue);
                nextValue++;
            }
        }

        return expected;
    }

    /// <summary>
    /// Generates a name-only enum scenario where all members are implicit.
    /// Expected values: 0, 1, 2, 3, ...
    /// </summary>
    private static Gen<EnumSequentialValueScenario> GenAllNameOnlyScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 6)
               from memberNames in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               let members = memberNames
                   .Select(n => (Name: n, ExplicitValue: (long?)null))
                   .ToList()
               let expected = ComputeExpectedValues(members)
               select new EnumSequentialValueScenario(name, members, expected);
    }

    /// <summary>
    /// Generates a mixed enum scenario where some members have explicit values
    /// and others are implicit. Tests that implicit members continue from
    /// the previous explicit value + 1.
    /// </summary>
    private static Gen<EnumSequentialValueScenario> GenMixedScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(2, 6)
               from memberNames in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from explicitIndices in GenExplicitIndices(memberCount)
               from baseValue in Gen.Choose(0, 50)
               let members = BuildMixedMembers(memberNames, explicitIndices, baseValue)
               let expected = ComputeExpectedValues(members)
               select new EnumSequentialValueScenario(name, members, expected);
    }

    /// <summary>
    /// Generates a scenario where an explicit value is followed by implicit members.
    /// Specifically tests Requirement 4.3: implicit after explicit gets explicit + 1.
    /// </summary>
    private static Gen<EnumSequentialValueScenario> GenExplicitThenImplicitScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(2, 5)
               from memberNames in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from explicitValue in Gen.Choose(0, 100)
               let members = memberNames.Select((n, i) =>
                   i == 0
                       ? (Name: n, ExplicitValue: (long?)explicitValue)
                       : (Name: n, ExplicitValue: (long?)null))
                   .ToList()
               let expected = ComputeExpectedValues(members)
               select new EnumSequentialValueScenario(name, members, expected);
    }

    /// <summary>
    /// Generates indices that should have explicit values (at least 1, not all).
    /// </summary>
    private static Gen<HashSet<int>> GenExplicitIndices(int memberCount)
    {
        // Pick 1 to memberCount-1 indices to be explicit (ensure at least one implicit).
        return from count in Gen.Choose(1, Math.Max(1, memberCount - 1))
               from indices in Gen.Shuffle(Enumerable.Range(0, memberCount).ToArray())
                   .Select(arr => arr.Take(count).ToHashSet())
               select indices;
    }

    /// <summary>
    /// Builds a member list with explicit values at specified indices.
    /// Explicit values are assigned incrementally from baseValue with gaps
    /// to make the test interesting.
    /// </summary>
    private static IReadOnlyList<(string Name, long? ExplicitValue)> BuildMixedMembers(
        string[] memberNames,
        HashSet<int> explicitIndices,
        int baseValue)
    {
        var members = new List<(string Name, long? ExplicitValue)>();
        var nextExplicit = (long)baseValue;

        for(var i = 0; i < memberNames.Length; i++)
        {
            if(explicitIndices.Contains(i))
            {
                members.Add((memberNames[i], nextExplicit));
                nextExplicit += 10; // Gap of 10 to make sequencing visible.
            }
            else
            {
                members.Add((memberNames[i], null));
            }
        }

        return members;
    }

    private static Gen<EnumSequentialValueScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAllNameOnlyScenario(),
            GenMixedScenario(),
            GenExplicitThenImplicitScenario());
    }

    public static Arbitrary<EnumSequentialValueScenario> ArbitraryEnumSequentialValueScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(long value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value.ToString()),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    private static EnumMemberNode MakeEnumMember(string name, ExpressionNode? value = null) =>
        new()
        {
            Name = Tok(name),
            Value = value,
        };

    private static EnumMemberListNode MakeEnumMemberList(IReadOnlyList<EnumMemberNode> members)
    {
        var first = members[0];
        var rest = members.Skip(1).ToImmutableArray();
        return new EnumMemberListNode
        {
            EnumMember = first,
            EnumMembers = rest,
        };
    }

    private static CompilationUnitNode BuildAst(EnumSequentialValueScenario scenario)
    {
        var members = new List<EnumMemberNode>();

        foreach(var (name, explicitValue) in scenario.Members)
        {
            var value = explicitValue.HasValue
                ? MakeIntLiteralExpression(explicitValue.Value)
                : null;
            members.Add(MakeEnumMember(name, value));
        }

        var memberList = MakeEnumMemberList(members);

        // No type annotation — let the builder infer the underlying type.
        var enumDecl = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumName),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = null,
            Members = memberList,
        };

        var typeDecl = new EnumTypeTypeDeclarationNode
        {
            EnumDeclaration = enumDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(typeDecl).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Sequential value assignment
    //
    // **Validates: Requirements 4.1, 4.2, 4.3**
    //
    // For any enum with N members where some or all members lack explicit
    // values, each implicit member SHALL receive a value equal to the
    // previous member's value plus 1 (with the first implicit member
    // receiving 0 if it is the first member overall, or previous+1
    // otherwise).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumSequentialValuePropertyTests) },
        MaxTest = 100)]
    public Property MemberValues_FollowSequentialAssignmentRules(
        EnumSequentialValueScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the enum symbol in the semantic model.
        var enumSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.EnumName && s.Kind == SymbolKind.Enum);

        if(enumSymbol is null)
        {
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' not found in semantic model. " +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        if(enumSymbol.DeclaredType is not EnumType enumType)
        {
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' DeclaredType is not EnumType, got: {enumSymbol.DeclaredType?.GetType().Name ?? "null"}");
        }

        if(enumType.Members.Count != scenario.Members.Count)
        {
            return false.ToProperty()
                .Label($"Expected {scenario.Members.Count} members, got {enumType.Members.Count}");
        }

        // Verify each member's resolved value matches the expected sequential value.
        for(var i = 0; i < scenario.Members.Count; i++)
        {
            var member = enumType.Members[i];
            var expectedValue = scenario.ExpectedValues[i];

            if(member.ResolvedValue is not long actualValue)
            {
                return false.ToProperty()
                    .Label($"Member '{member.Name}' at index {i}: ResolvedValue is not a long. " +
                           $"Got: {member.ResolvedValue?.GetType().Name ?? "null"} = {member.ResolvedValue}. " +
                           $"Scenario: {scenario}");
            }

            if(actualValue != expectedValue)
            {
                return false.ToProperty()
                    .Label($"Member '{member.Name}' at index {i}: expected value {expectedValue}, " +
                           $"got {actualValue}. Scenario: {scenario}");
            }
        }

        return true.ToProperty()
            .Label($"All {scenario.Members.Count} members have correct sequential values");
    }
}