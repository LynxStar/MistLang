// Feature: abstract-type-unification, Property 3: Slot Satisfaction Completeness
// Feature: abstract-type-unification, Property 4: Signature Matching Exactness
// Feature: abstract-type-unification, Property 5: Inherited Slot Propagation
// **Validates: Requirements 14.1, 14.2, 14.3, 14.4, 14.5, 14.6**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying slot-satisfaction validation in the presence of
/// default method implementations (Phase 3 of abstract-type-unification).
///
/// These tests exercise the semantic model builder in-process (no external process
/// spawning) by constructing ASTs directly and inspecting the emitted diagnostics.
///
/// Property 3 (Slot Satisfaction Completeness): a concrete type declaring
/// <c>: AbstractType</c> is accepted iff it provides implementations for every
/// non-default slot. Default methods need no implementation.
///
/// Property 4 (Signature Matching Exactness): a candidate implementation satisfies a
/// slot iff its name, return type, and all parameter types match exactly. An exact
/// override of a default method is accepted (filling takes precedence).
///
/// Property 5 (Inherited Slot Propagation): non-default slots from the entire
/// abstract inheritance chain are required; defaults at any level are not.
/// </summary>
public class AbstractDefaultMethodSatisfactionPropertyTests
{
    private const string MissingMethodDiagnosticId = "MIST_SM_0022";

    private readonly SemanticModelBuilder builder = new();

    // ─── Low-level AST helpers ───────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        };
    }

    private static TypeReferenceNode VoidType()
    {
        return new() { First = new VoidSingleTypeRefSingleTypeRefNode() };
    }

    private static TypeReferenceNode IntType()
    {
        return new()
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ImmutableArray<Diagnostic> BuildAndCollectDiagnostics(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        _ = builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return diagnostics.ToImmutableArray();
    }

    private static int CountMissingMethodDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Count(d => d.Id.Value == MissingMethodDiagnosticId);
    }

    // ─── Declaration construction helpers ────────────────────────────────

    /// <summary>A slot: an abstract method with no body.</summary>
    private static AbstractMethodDeclarationNode MakeSlot(string name, TypeReferenceNode? returnType = null)
    {
        return new()
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? VoidType(),
            Parameters = null,
        };
    }

    /// <summary>A concrete method (with body), used for default methods and for impls.</summary>
    private static MethodDeclarationNode MakeMethod(string name, TypeReferenceNode? returnType = null)
    {
        return new()
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? VoidType(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Builds an abstract declaration with the given slot names (no body) and default
    /// method names (with body), optionally inheriting from a parent abstract.
    /// </summary>
    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        IEnumerable<string> slotNames,
        IEnumerable<string> defaultNames,
        string? parentAbstractName = null)
    {
        var members = ImmutableArray.CreateBuilder<AbstractMemberDeclarationNode>();

        foreach(var slot in slotNames)
        {
            members.Add(new AbstractMethodMemberAbstractMemberDeclarationNode { Method = MakeSlot(slot) });
        }

        foreach(var def in defaultNames)
        {
            members.Add(new AbstractDefaultMethodMemberAbstractMemberDeclarationNode { DefaultMethod = MakeMethod(def) });
        }

        var decl = new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = members.ToImmutable(),
        };

        if(parentAbstractName is not null)
        {
            var parentList = MakeBaseList(parentAbstractName);
            decl.BaseClause = parentList;
            decl.BaseList = parentList;
        }

        return decl;
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string abstractName,
        IEnumerable<MethodDeclarationNode> methods)
    {
        var members = methods
            .Select(m => (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m })
            .ToImmutableArray();

        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        var baseList = MakeBaseList(abstractName);
        decl.BaseClause = baseList;
        decl.BaseList = baseList;
        return decl;
    }

    private static TypeDeclarationNode WrapAbstract(AbstractDeclarationNode decl)
    {
        return new AbstractTypeTypeDeclarationNode { AbstractDeclaration = decl };
    }

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl)
    {
        return new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new() { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
    }

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario: an abstract with disjoint slot and default method name sets, and a
    /// subset of slots the concrete type chooses to implement.
    /// </summary>
    public record DefaultScenario(
        ImmutableArray<string> SlotNames,
        ImmutableArray<string> DefaultNames,
        ImmutableArray<string> ProvidedSlotNames)
    {
        public ImmutableArray<string> MissingSlotNames =>
            SlotNames.Where(s => !ProvidedSlotNames.Contains(s)).ToImmutableArray();

        public override string ToString()
        {
            static string join(ImmutableArray<string> xs)
            {
                return xs.Length == 0 ? "(none)" : string.Join(", ", xs);
            }

            return $"slots[{join(SlotNames)}] defaults[{join(DefaultNames)}] provided[{join(ProvidedSlotNames)}]";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools (disjoint so slot/default names never collide) ───────

    private static readonly string[] SlotNamePool =
        ["DoWork", "Process", "Execute", "Run"];

    private static readonly string[] DefaultNamePool =
        ["Describe", "Summarize", "Report", "Trace"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates an abstract with 0-4 slots and 0-4 defaults (disjoint name pools),
    /// where the concrete type provides a random subset of the slots and none of the
    /// defaults.
    /// </summary>
    public static Gen<DefaultScenario> GenDefaultScenario()
    {
        return from slotCount in Gen.Choose(0, SlotNamePool.Length)
               from defaultCount in Gen.Choose(0, DefaultNamePool.Length)
               from shuffledSlots in Gen.Shuffle(SlotNamePool)
               from shuffledDefaults in Gen.Shuffle(DefaultNamePool)
               let slots = shuffledSlots.Take(slotCount).ToImmutableArray()
               let defaults = shuffledDefaults.Take(defaultCount).ToImmutableArray()
               from providedCount in Gen.Choose(0, slotCount)
               from providedShuffle in Gen.Shuffle(slots.ToArray())
               let provided = providedShuffle.Take(providedCount).ToImmutableArray()
               select new DefaultScenario(slots, defaults, provided);
    }

    public static Arbitrary<DefaultScenario> ArbitraryDefaultScenario()
    {
        return GenDefaultScenario().ToArbitrary();
    }

    // ─── Property 3: Slot Satisfaction Completeness ──────────────────────

    /// <summary>
    /// Property 3: A concrete type is accepted (no missing-method diagnostic) if and
    /// only if every non-default slot is implemented. The number of diagnostics equals
    /// the number of unfilled slots; default methods never contribute a diagnostic and
    /// never appear in a diagnostic message.
    ///
    /// **Validates: Requirements 14.1, 14.2, 14.3, 14.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractDefaultMethodSatisfactionPropertyTests) }, MaxTest = 200)]
    public bool SlotSatisfaction_DefaultsNeverRequired(DefaultScenario scenario)
    {
        var abstractDecl = MakeAbstractDecl("IThing", scenario.SlotNames, scenario.DefaultNames);

        // The concrete type implements only the chosen subset of slots — never any default.
        var implMethods = scenario.ProvidedSlotNames.Select(n => MakeMethod(n));
        var classDecl = MakeClassDecl("Thing", "IThing", implMethods);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);
        var missing = diagnostics.Where(d => d.Id.Value == MissingMethodDiagnosticId).ToList();

        // Exactly one diagnostic per unfilled slot.
        if(missing.Count != scenario.MissingSlotNames.Length)
        {
            return false;
        }

        // Every unfilled slot name is reported.
        foreach(var slot in scenario.MissingSlotNames)
        {
            if(!missing.Any(d => d.Message.Contains(slot)))
            {
                return false;
            }
        }

        // No default method name ever appears in a missing-method diagnostic.
        foreach(var def in scenario.DefaultNames)
        {
            if(missing.Any(d => d.Message.Contains(def)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 3 (corollary): when ALL slots are implemented, the type is accepted
    /// regardless of how many defaults exist and regardless of the type implementing
    /// none of them.
    ///
    /// **Validates: Requirements 14.1, 14.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractDefaultMethodSatisfactionPropertyTests) }, MaxTest = 200)]
    public bool AllSlotsFilled_DefaultsOmitted_Accepted(DefaultScenario scenario)
    {
        var abstractDecl = MakeAbstractDecl("IThing", scenario.SlotNames, scenario.DefaultNames);

        // Provide ALL slots, no defaults.
        var implMethods = scenario.SlotNames.Select(n => MakeMethod(n));
        var classDecl = MakeClassDecl("Thing", "IThing", implMethods);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);

        return CountMissingMethodDiagnostics(diagnostics) == 0;
    }

    // ─── Property 4: Signature Matching Exactness ────────────────────────

    /// <summary>
    /// Property 4: For a single slot requiring <c>int M()</c>, a candidate
    /// implementation is accepted iff its return type matches exactly. An exact match
    /// produces no diagnostic; a return-type mismatch (void vs int) produces a
    /// missing-method diagnostic.
    ///
    /// **Validates: Requirements 14.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractDefaultMethodSatisfactionPropertyTests) }, MaxTest = 100)]
    public bool SlotSignature_AcceptedIffReturnTypeMatchesExactly(bool returnTypesMatch)
    {
        // Slot: int Compute()
        var slot = MakeSlot("Compute", IntType());
        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken("IComputer"),
            Members = ImmutableArray.Create<AbstractMemberDeclarationNode>(
                new AbstractMethodMemberAbstractMemberDeclarationNode { Method = slot }),
        };

        // Impl: int Compute() (match) OR void Compute() (mismatch)
        var implReturn = returnTypesMatch ? IntType() : VoidType();
        var classDecl = MakeClassDecl("Computer", "IComputer", new[] { MakeMethod("Compute", implReturn) });

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);
        var missingCount = CountMissingMethodDiagnostics(diagnostics);

        // Accepted (0 diagnostics) iff the signatures match exactly.
        return returnTypesMatch ? missingCount == 0 : missingCount == 1;
    }

    /// <summary>
    /// Property 4 (override precedence): a concrete type that provides an exact-signature
    /// implementation of a DEFAULT method is accepted (filling takes precedence over the
    /// default), and a concrete type that omits the default is equally accepted.
    ///
    /// **Validates: Requirements 14.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractDefaultMethodSatisfactionPropertyTests) }, MaxTest = 100)]
    public bool DefaultOverride_ExactSignature_Accepted(bool overridesDefault)
    {
        // Abstract with one slot and one default method.
        var abstractDecl = MakeAbstractDecl(
            "IThing",
            slotNames: new[] { "DoWork" },
            defaultNames: new[] { "Describe" });

        // Always fill the required slot. Optionally also override the default.
        var methods = new List<MethodDeclarationNode> { MakeMethod("DoWork") };
        if(overridesDefault)
        {
            methods.Add(MakeMethod("Describe"));
        }

        var classDecl = MakeClassDecl("Thing", "IThing", methods);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);

        // Accepted whether or not the default is overridden.
        return CountMissingMethodDiagnostics(diagnostics) == 0;
    }

    // ─── Property 5: Inherited Slot Propagation ──────────────────────────

    /// <summary>
    /// Property 5: For a child abstract inheriting from a parent abstract, an implementer
    /// of the child must fill all non-default slots from BOTH levels, but is never
    /// required to fill defaults declared at either level. With all slots filled and no
    /// defaults filled, the type is accepted.
    ///
    /// **Validates: Requirements 14.3, 14.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractDefaultMethodSatisfactionPropertyTests) }, MaxTest = 200)]
    public bool InheritedDefaults_NotRequired_InheritedSlotsRequired(DefaultScenario scenario)
    {
        // Split the slot pool across parent and child so names stay disjoint.
        var parentSlots = scenario.SlotNames.Where((_, i) => i % 2 == 0).ToImmutableArray();
        var childSlots = scenario.SlotNames.Where((_, i) => i % 2 == 1).ToImmutableArray();
        var parentDefaults = scenario.DefaultNames.Where((_, i) => i % 2 == 0).ToImmutableArray();
        var childDefaults = scenario.DefaultNames.Where((_, i) => i % 2 == 1).ToImmutableArray();

        var parentDecl = MakeAbstractDecl("IParent", parentSlots, parentDefaults);
        var childDecl = MakeAbstractDecl("IChild", childSlots, childDefaults, parentAbstractName: "IParent");

        // Implementer fills ALL slots (parent + child), and NONE of the defaults.
        var allSlots = parentSlots.Concat(childSlots);
        var implMethods = allSlots.Select(n => MakeMethod(n));
        var classDecl = MakeClassDecl("Impl", "IChild", implMethods);

        var ast = MakeCompilationUnit(
            WrapAbstract(parentDecl),
            WrapAbstract(childDecl),
            WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);

        return CountMissingMethodDiagnostics(diagnostics) == 0;
    }

    /// <summary>
    /// Property 5 (negative): when an implementer of the child omits an inherited PARENT
    /// slot, exactly one missing-method diagnostic is emitted for that slot — proving
    /// inherited non-default slots are still required.
    ///
    /// **Validates: Requirements 14.2**
    /// </summary>
    [Fact]
    public void InheritedParentSlot_Missing_EmitsDiagnostic()
    {
        var parentDecl = MakeAbstractDecl(
            "IParent",
            slotNames: new[] { "BaseSlot" },
            defaultNames: new[] { "BaseDefault" });
        var childDecl = MakeAbstractDecl(
            "IChild",
            slotNames: new[] { "ChildSlot" },
            defaultNames: new[] { "ChildDefault" },
            parentAbstractName: "IParent");

        // Implementer fills only the child slot, omits the inherited parent slot and
        // all defaults.
        var classDecl = MakeClassDecl("Impl", "IChild", new[] { MakeMethod("ChildSlot") });

        var ast = MakeCompilationUnit(
            WrapAbstract(parentDecl),
            WrapAbstract(childDecl),
            WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);
        var missing = diagnostics.Where(d => d.Id.Value == MissingMethodDiagnosticId).ToList();

        _ = Assert.Single(missing);
        Assert.Contains("BaseSlot", missing[0].Message);
        Assert.DoesNotContain(missing, d => d.Message.Contains("BaseDefault"));
        Assert.DoesNotContain(missing, d => d.Message.Contains("ChildDefault"));
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void AbstractWithOnlyDefaults_EmptyImplementer_Accepted()
    {
        // An abstract whose every method has a default — an empty implementer is valid.
        var abstractDecl = MakeAbstractDecl(
            "IDefaulted",
            slotNames: Array.Empty<string>(),
            defaultNames: new[] { "Describe", "Summarize" });
        var classDecl = MakeClassDecl("Impl", "IDefaulted", Array.Empty<MethodDeclarationNode>());

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);

        Assert.Equal(0, CountMissingMethodDiagnostics(diagnostics));
    }

    [Fact]
    public void MixedSlotsAndDefaults_OnlySlotFilled_Accepted()
    {
        var abstractDecl = MakeAbstractDecl(
            "IMixed",
            slotNames: new[] { "DoWork" },
            defaultNames: new[] { "Describe" });
        var classDecl = MakeClassDecl("Impl", "IMixed", new[] { MakeMethod("DoWork") });

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);

        Assert.Equal(0, CountMissingMethodDiagnostics(diagnostics));
    }

    [Fact]
    public void MixedSlotsAndDefaults_SlotOmitted_EmitsDiagnosticForSlotOnly()
    {
        var abstractDecl = MakeAbstractDecl(
            "IMixed",
            slotNames: new[] { "DoWork" },
            defaultNames: new[] { "Describe" });
        // Implementer provides neither — only the slot should be flagged.
        var classDecl = MakeClassDecl("Impl", "IMixed", Array.Empty<MethodDeclarationNode>());

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var diagnostics = BuildAndCollectDiagnostics(ast);
        var missing = diagnostics.Where(d => d.Id.Value == MissingMethodDiagnosticId).ToList();

        _ = Assert.Single(missing);
        Assert.Contains("DoWork", missing[0].Message);
        Assert.DoesNotContain(missing, d => d.Message.Contains("Describe"));
    }
}