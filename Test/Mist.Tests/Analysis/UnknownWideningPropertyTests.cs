// Feature: unknown-type (Phase D, Task 23.6 + 23.7)
// **Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.6**
//
// Property-based and unit tests for covariant unknown widening in
// TypeCheckerAnalyzer.IsAssignable / IsDirectlyAssignable. The rule has
// five parts:
//
//   Req 8.1 — `Foo<A_i>` → `Foo<B_i>` when for each i, either A_i == B_i
//             or B_i is UnknownType, AND Foo.Kind is TypeKind.Class.
//   Req 8.2 — applies transitively through the inheritance hierarchy
//             (delegated to the existing base-type loop).
//   Req 8.3 — applies to nested generic positions; nested widening is
//             independent of whether the outer generic is a class or
//             struct.
//   Req 8.4 — `IsDirectlyAssignable` accepts the class widening so that
//             a trivial cast `(List<unknown>) intList` does not require
//             a user-declared cast operator.
//   Req 8.5 — the rule is one-directional: source-side UnknownType does
//             NOT widen to a concrete target position.
//   Req 8.6 — widening on a struct generic is rejected with MIST_TC_0058
//             at the widening site.
//
// All tests in this file run entirely in-process against IsAssignable
// and IsDirectlyAssignable (no compilation, no process spawning), as
// permitted by the InternalsVisibleTo directive on Mist.csproj. The
// MIST_TC_0058 emission is exercised at the end via the same in-process
// SemanticModelBuilder + TypeCheckerAnalyzer flow used by
// UnknownMemberFilteringTests, so no additional process cost is
// incurred.

using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for covariant unknown widening.
///
/// <para>
/// <strong>Property 5 (unknown-type feature):</strong> covariant unknown
/// widening is position-wise and class-only. For any class-generic
/// definition <c>Foo</c> and any tuple of type arguments, replacing any
/// subset of positions with <see cref="UnknownType"/> produces a target
/// type to which the original is assignable. For any struct-generic
/// definition, widening with any non-empty subset of positions replaced
/// by <see cref="UnknownType"/> fails.
/// <strong>Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.6</strong>
/// </para>
///
/// <para>
/// The property runs in-process against
/// <see cref="TypeCheckerAnalyzer.IsAssignable(IType, IType)"/>,
/// generating synthetic constructed types from a small pool of class and
/// struct generic definitions (see <see cref="ListClassDef"/>,
/// <see cref="BoxClassDef"/>, <see cref="MapClassDef"/>,
/// <see cref="OptionalStructDef"/>, <see cref="PairStructDef"/>).
/// </para>
/// </summary>
public class UnknownWideningPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Synthetic type pool
    //
    // A small fixed set of generic definitions — two class-generics with
    // arity 1 and 2, and two struct-generics with arity 1 and 2 — plus a
    // multi-arg class (Map). Type arguments are drawn from a small pool
    // of primitive-equivalent types so the assignability check is
    // meaningful without depending on the stdlib.
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly ClassType ListClassDef = BuildClassDef("List", 1);
    private static readonly ClassType BoxClassDef = BuildClassDef("Box", 1);
    private static readonly ClassType MapClassDef = BuildClassDef("Map", 2);

    private static readonly StructType OptionalStructDef = BuildStructDef("Optional", 1);
    private static readonly StructType PairStructDef = BuildStructDef("Pair", 2);

    private static readonly ClassType SampleClassA =
        new("SampleA", new Scope(parent: null), containingNamespace: "TestPool");
    private static readonly ClassType SampleClassB =
        new("SampleB", new Scope(parent: null), containingNamespace: "TestPool");

    private static ClassType BuildClassDef(string name, int arity)
    {
        var scope = new Scope(parent: null);
        var def = new ClassType(name, scope, containingNamespace: "TestPool");
        var parameters = new TypeParameterType[arity];
        for(var i = 0; i < arity; i++)
        {
            parameters[i] = new TypeParameterType($"T{i}", i);
        }

        def.TypeParameters = parameters;
        return def;
    }

    private static StructType BuildStructDef(string name, int arity)
    {
        var scope = new Scope(parent: null);
        var def = new StructType(name, scope, containingNamespace: "TestPool");
        var parameters = new TypeParameterType[arity];
        for(var i = 0; i < arity; i++)
        {
            parameters[i] = new TypeParameterType($"T{i}", i);
        }

        def.TypeParameters = parameters;
        return def;
    }

    // Pool of concrete (non-unknown) type arguments.
    private static readonly IType[] ConcreteArgPool = new IType[]
    {
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.String,
        SampleClassA,
        SampleClassB,
    };

#pragma warning disable CA1034 // Nested type is a test fixture DTO.
    /// <summary>
    /// A generated scenario for the class-widening property: a pool-drawn
    /// generic definition, a tuple of concrete type arguments, and a
    /// bitmask describing which positions to replace with
    /// <see cref="UnknownType"/> on the target side.
    /// </summary>
    public sealed record ClassWideningScenario(
        ClassType Definition,
        IReadOnlyList<IType> SourceArgs,
        int UnknownMask)
    {
        public override string ToString()
        {
            return $"{Definition.DisplayName}<{string.Join(", ", SourceArgs.Select(a => a.DisplayName))}> → mask 0b{Convert.ToString(UnknownMask, 2)}";
        }
    }

    /// <summary>
    /// A generated scenario for the struct-rejection property: same shape
    /// as <see cref="ClassWideningScenario"/> but over a struct-generic
    /// definition and with <see cref="UnknownMask"/> guaranteed to have
    /// at least one bit set (so a widening actually happens at the struct
    /// level and Req 8.6 can fire).
    /// </summary>
    public sealed record StructWideningScenario(
        StructType Definition,
        IReadOnlyList<IType> SourceArgs,
        int UnknownMask)
    {
        public override string ToString()
        {
            return $"{Definition.DisplayName}<{string.Join(", ", SourceArgs.Select(a => a.DisplayName))}> → mask 0b{Convert.ToString(UnknownMask, 2)}";
        }
    }

    /// <summary>
    /// A generated scenario for the reverse-direction rejection property:
    /// the source side has <see cref="UnknownType"/> at positions given by
    /// <see cref="UnknownMask"/>, and the target side has concrete args.
    /// The mask always has at least one bit set so the widening is not
    /// the identity case.
    /// </summary>
    public sealed record ReverseWideningScenario(
        ClassType Definition,
        IReadOnlyList<IType> TargetArgs,
        int UnknownMask)
    {
        public override string ToString()
        {
            return $"{Definition.DisplayName}<…> (source mask 0b{Convert.ToString(UnknownMask, 2)}) → {Definition.DisplayName}<{string.Join(", ", TargetArgs.Select(a => a.DisplayName))}>";
        }
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly ClassType[] ClassDefs =
    {
        ListClassDef,
        BoxClassDef,
        MapClassDef,
    };

    private static readonly StructType[] StructDefs =
    {
        OptionalStructDef,
        PairStructDef,
    };

    public static Arbitrary<ClassWideningScenario> ArbitraryClassWidening()
    {
        var gen =
            from def in Gen.Elements(ClassDefs)
            let arity = def.TypeParameters.Count
            from args in Gen.ArrayOf(Gen.Elements(ConcreteArgPool), arity)
                // Mask range is 0..(2^arity - 1) — all subsets, including the
                // empty subset (identity case, no widening) so the identity
                // path is also exercised.
            from mask in Gen.Choose(0, (1 << arity) - 1)
            select new ClassWideningScenario(def, args, mask);
        return gen.ToArbitrary();
    }

    public static Arbitrary<StructWideningScenario> ArbitraryStructWidening()
    {
        var gen =
            from def in Gen.Elements(StructDefs)
            let arity = def.TypeParameters.Count
            from args in Gen.ArrayOf(Gen.Elements(ConcreteArgPool), arity)
                // Mask range is 1..(2^arity - 1) — at least one position
                // must be widened so Req 8.6 fires at this level.
            from mask in Gen.Choose(1, (1 << arity) - 1)
            select new StructWideningScenario(def, args, mask);
        return gen.ToArbitrary();
    }

    public static Arbitrary<ReverseWideningScenario> ArbitraryReverseWidening()
    {
        var gen =
            from def in Gen.Elements(ClassDefs)
            let arity = def.TypeParameters.Count
            from args in Gen.ArrayOf(Gen.Elements(ConcreteArgPool), arity)
                // Mask has at least one bit set so some source position is
                // UnknownType while the target position is concrete.
            from mask in Gen.Choose(1, (1 << arity) - 1)
            select new ReverseWideningScenario(def, args, mask);
        return gen.ToArbitrary();
    }

    private static IType[] ApplyMask(IReadOnlyList<IType> args, int mask)
    {
        var result = new IType[args.Count];
        for(var i = 0; i < args.Count; i++)
        {
            result[i] = ((mask >> i) & 1) == 1 ? (IType)UnknownType.Instance : args[i];
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5a — class widening is accepted position-wise.
    //
    // **Validates: Requirements 8.1, 8.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(UnknownWideningPropertyTests) }, MaxTest = 100)]
    public bool ClassGeneric_WidensPositionWise(ClassWideningScenario scenario)
    {
        var sourceArgs = scenario.SourceArgs.ToArray();
        var targetArgs = ApplyMask(scenario.SourceArgs, scenario.UnknownMask);

        var source = new ConstructedType(scenario.Definition, sourceArgs);
        var target = new ConstructedType(scenario.Definition, targetArgs);

        return TypeCheckerAnalyzer.IsAssignable(source, target);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5b — struct widening is always rejected by IsAssignable.
    //
    // **Validates: Requirements 8.1, 8.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(UnknownWideningPropertyTests) }, MaxTest = 100)]
    public bool StructGeneric_NeverWidens(StructWideningScenario scenario)
    {
        var sourceArgs = scenario.SourceArgs.ToArray();
        var targetArgs = ApplyMask(scenario.SourceArgs, scenario.UnknownMask);

        var source = new ConstructedType(scenario.Definition, sourceArgs);
        var target = new ConstructedType(scenario.Definition, targetArgs);

        // Req 8.1 + 8.6: struct-generic widening must not succeed under
        // IsAssignable. The targeted MIST_TC_0058 diagnostic is exercised
        // by the integration-style unit test below.
        return !TypeCheckerAnalyzer.IsAssignable(source, target);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5c — reverse direction (source-side UnknownType widening
    // to a concrete target position) is always rejected.
    //
    // **Validates: Requirement 8.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(UnknownWideningPropertyTests) }, MaxTest = 100)]
    public bool ReverseWidening_IsAlwaysRejected(ReverseWideningScenario scenario)
    {
        var targetArgs = scenario.TargetArgs.ToArray();
        var sourceArgs = ApplyMask(scenario.TargetArgs, scenario.UnknownMask);

        var source = new ConstructedType(scenario.Definition, sourceArgs);
        var target = new ConstructedType(scenario.Definition, targetArgs);

        // Req 8.5: even for class generics, a source-side UnknownType
        // must NOT widen to a concrete target position.
        return !TypeCheckerAnalyzer.IsAssignable(source, target);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 23.7 — Unit tests for nested widening and the defCtx rule
    //
    // **Validates: Requirements 8.1, 8.3, 8.5, 8.6**
    //
    // The property tests above cover position-wise widening at a single
    // level. The unit tests below pin down the nested-defCtx rule that
    // drives the interesting case from the design document: a struct-
    // generic outer and a class-generic inner, where the widening happens
    // at the inner (class) boundary and is therefore accepted.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Req 8.1 / 8.3 — <c>Optional&lt;List&lt;int&gt;&gt;</c> →
    /// <c>Optional&lt;List&lt;unknown&gt;&gt;</c> when <c>Optional</c> is
    /// a struct and <c>List</c> is a class. The widening happens at the
    /// inner <c>List</c> position, whose <c>defCtx</c> is the class
    /// generic <c>List</c>, so the class-only rule is satisfied.
    /// </summary>
    [Fact]
    public void NestedWidening_StructOuterClassInner_IsAccepted()
    {
        var listOfInt = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });
        var listOfUnknown = new ConstructedType(ListClassDef, new IType[] { UnknownType.Instance });

        var optOfListOfInt = new ConstructedType(OptionalStructDef, new IType[] { listOfInt });
        var optOfListOfUnknown = new ConstructedType(OptionalStructDef, new IType[] { listOfUnknown });

        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(optOfListOfInt, optOfListOfUnknown),
            "Optional<List<int>> → Optional<List<unknown>> must be accepted: the outer Optional position compares List<int> vs List<unknown>, which recurses into the class widening on List (Req 8.3).");
    }

    /// <summary>
    /// Req 8.1 / 8.6 — <c>Optional&lt;int&gt;</c> →
    /// <c>Optional&lt;unknown&gt;</c> when <c>Optional</c> is a struct.
    /// The widening happens directly at the struct level, so the
    /// class-only rule rejects it and <see cref="TypeCheckerAnalyzer.IsAssignable(IType, IType)"/>
    /// returns false. The targeted MIST_TC_0058 diagnostic is exercised
    /// by the end-to-end test below.
    /// </summary>
    [Fact]
    public void DirectStructWidening_IsRejected()
    {
        var optOfInt = new ConstructedType(OptionalStructDef, new IType[] { LegacyPrimitiveTypes.Int });
        var optOfUnknown = new ConstructedType(OptionalStructDef, new IType[] { UnknownType.Instance });

        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(optOfInt, optOfUnknown),
            "Optional<int> → Optional<unknown> must be rejected when Optional is a struct (Req 8.1, 8.6).");
    }

    /// <summary>
    /// Req 8.1 — <c>List&lt;int&gt;</c> → <c>List&lt;unknown&gt;</c>
    /// when <c>List</c> is a class. The canonical accepting case.
    /// </summary>
    [Fact]
    public void ClassWidening_IsAccepted()
    {
        var listOfInt = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });
        var listOfUnknown = new ConstructedType(ListClassDef, new IType[] { UnknownType.Instance });

        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(listOfInt, listOfUnknown),
            "List<int> → List<unknown> must be accepted when List is a class (Req 8.1).");
    }

    /// <summary>
    /// Req 8.5 — <c>List&lt;unknown&gt;</c> → <c>List&lt;int&gt;</c> is
    /// rejected.
    /// </summary>
    [Fact]
    public void ReverseClassWidening_IsRejected()
    {
        var listOfInt = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });
        var listOfUnknown = new ConstructedType(ListClassDef, new IType[] { UnknownType.Instance });

        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(listOfUnknown, listOfInt),
            "List<unknown> → List<int> must be rejected (Req 8.5: the rule is one-directional).");
    }

    /// <summary>
    /// Req 8.4 — <see cref="TypeCheckerAnalyzer.IsDirectlyAssignable"/>
    /// also accepts the class widening so that a trivial cast
    /// <c>(List&lt;unknown&gt;) intList</c> does not require a
    /// user-declared cast operator.
    /// </summary>
    [Fact]
    public void IsDirectlyAssignable_AcceptsClassWidening()
    {
        var listOfInt = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });
        var listOfUnknown = new ConstructedType(ListClassDef, new IType[] { UnknownType.Instance });

        Assert.True(
            TypeCheckerAnalyzer.IsDirectlyAssignable(listOfInt, listOfUnknown, model: null),
            "IsDirectlyAssignable must accept List<int> → List<unknown> so the cast operator lookup is skipped (Req 8.4).");
    }

    /// <summary>
    /// Multi-arg class generic — <c>Map&lt;string, int&gt;</c> →
    /// <c>Map&lt;string, unknown&gt;</c>. Exercises the mixed case where
    /// one position widens and the other is identity.
    /// </summary>
    [Fact]
    public void MultiArgClassWidening_MixedPositions_IsAccepted()
    {
        var mapStringInt = new ConstructedType(
            MapClassDef, new IType[] { LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Int });
        var mapStringUnknown = new ConstructedType(
            MapClassDef, new IType[] { LegacyPrimitiveTypes.String, UnknownType.Instance });

        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(mapStringInt, mapStringUnknown),
            "Map<string, int> → Map<string, unknown> must be accepted (Req 8.1 — class generic, widen only the second position).");
    }

    /// <summary>
    /// Multi-arg struct generic — <c>Pair&lt;int, string&gt;</c> →
    /// <c>Pair&lt;int, unknown&gt;</c>. Direct struct widening at the
    /// <c>Pair</c> boundary is rejected.
    /// </summary>
    [Fact]
    public void MultiArgStructWidening_DirectLevel_IsRejected()
    {
        var pairIntStr = new ConstructedType(
            PairStructDef, new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String });
        var pairIntUnknown = new ConstructedType(
            PairStructDef, new IType[] { LegacyPrimitiveTypes.Int, UnknownType.Instance });

        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(pairIntStr, pairIntUnknown),
            "Pair<int, string> → Pair<int, unknown> must be rejected when Pair is a struct (Req 8.6).");
    }

    /// <summary>
    /// Identity case — <c>List&lt;int&gt;</c> → <c>List&lt;int&gt;</c>
    /// continues to work after the widening clause is added.
    /// </summary>
    [Fact]
    public void ClassIdentity_IsStillAccepted()
    {
        var a = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });
        var b = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });

        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(a, b),
            "List<int> → List<int> must continue to be accepted (identity after widening clause).");
    }

    /// <summary>
    /// Different concrete positions on a class generic —
    /// <c>List&lt;int&gt;</c> → <c>List&lt;string&gt;</c> must still be
    /// rejected; the widening clause only accepts identity or
    /// <c>UnknownType</c> on the target side.
    /// </summary>
    [Fact]
    public void ClassWithDifferentConcreteTypes_IsStillRejected()
    {
        var listOfInt = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.Int });
        var listOfStr = new ConstructedType(ListClassDef, new IType[] { LegacyPrimitiveTypes.String });

        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(listOfInt, listOfStr),
            "List<int> → List<string> must still be rejected — widening only accepts UnknownType on the target side.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 23.4 — End-to-end diagnostic emission for the struct-generic
    // restriction. Compiles a small Mist program in-process and asserts
    // that MIST_TC_0058 is produced at the widening site.
    //
    // **Validates: Requirement 8.6**
    // ═══════════════════════════════════════════════════════════════════════

    private const string StructGenericDiagnosticId = "MIST_TC_0058";

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// Lexes, parses, builds the semantic model, then runs the type
    /// checker against <paramref name="source"/>. Returns the combined
    /// diagnostic set. Modelled on the helper in
    /// <c>UnknownMemberFilteringTests</c> — no process spawn.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndCollectDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var builder = new SemanticModelBuilder();
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var analyzer = new TypeCheckerAnalyzer();
        var tcBag = new DiagnosticBag();
        analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray()
            .Concat(tcBag.ToImmutableArray())
            .ToImmutableArray();
    }

    /// <summary>
    /// <c>Optional&lt;int&gt;</c> → <c>Optional&lt;unknown&gt;</c> at an
    /// assignment site must emit <c>MIST_TC_0058</c>. The user-declared
    /// <c>struct Optional&lt;T&gt;</c> in the program ensures the
    /// generic definition being widened is a struct, so Req 8.6 fires.
    /// </summary>
    [Fact]
    public void StructGenericWidening_EmitsMIST_TC_0058_At_Assignment()
    {
        // A user-declared struct generic `Optional<T>` keeps the test
        // independent of the stdlib (whose `Optional<T>` is a class).
        // The widening happens at a local-variable assignment, one of
        // the widening sites where TryEmitUnknownAssignabilityDiagnostic
        // is invoked (Task 23.4).
        const string source =
            "struct Optional<T> { public T value; }\n" +
            "class Foo {\n" +
            "    public void M(Optional<int> oi) {\n" +
            "        Optional<unknown> ou = oi;\n" +
            "    }\n" +
            "}\n";

        var diagnostics = CompileAndCollectDiagnostics(source);

        Assert.Contains(
            diagnostics,
            d => d.Id.Value == StructGenericDiagnosticId);
    }

    /// <summary>
    /// Control case — the same shape with a class-generic outer
    /// (<c>List&lt;int&gt;</c> → <c>List&lt;unknown&gt;</c> via a local
    /// declaration) must NOT emit <c>MIST_TC_0058</c>.
    /// </summary>
    [Fact]
    public void ClassGenericWidening_DoesNotEmit_MIST_TC_0058()
    {
        const string source =
            "class List<T> { public T item; }\n" +
            "class Foo {\n" +
            "    public void M(List<int> li) {\n" +
            "        List<unknown> lu = li;\n" +
            "    }\n" +
            "}\n";

        var diagnostics = CompileAndCollectDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id.Value == StructGenericDiagnosticId);
    }
}