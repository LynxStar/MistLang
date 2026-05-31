// Feature: traits, Property 6: Trait assignability
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying trait assignability in the type checker.
///
/// Property 6: For any type that satisfies a trait (structurally or via a with block),
/// IsAssignable(sourceType, traitType) SHALL return true. For any type that does not
/// satisfy a trait, IsAssignable(sourceType, traitType) SHALL return false. This applies
/// to variable assignments, method parameter passing, and return type checking.
/// </summary>
public class TraitAssignabilityPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
            "string" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            "bool" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            } },
            _ => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
        };
    }

    // ─── Expression builders ─────────────────────────────────────────────

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    // ─── Statement builders ──────────────────────────────────────────────

    /// <summary>
    /// Creates: DeclaredType varName = new InitializedType();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
                } },
                Name = Tok(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode> statements)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = statements },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    private static ClassDeclarationNode MakeProgramClass(ImmutableArray<StatementNode> mainStatements)
    {
        var mainMethod = MakeMainMethod(mainStatements);
        return new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    private static TopLevelItemNode MakeClassTypeItem(ClassDeclarationNode classDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        };

    private static TopLevelItemNode MakeStructTypeItem(StructDeclarationNode structDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new StructTypeTypeDeclarationNode { StructDeclaration = structDecl },
        };

    private static TopLevelItemNode MakeTraitTypeItem(TraitDeclarationNode traitDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        };

    private static TopLevelItemNode MakeWithItem(WithDeclarationNode withDecl) =>
        new WithItemTopLevelItemNode { WithDeclaration = withDecl };

    private static ClassDeclarationNode MakeClassWithPublicFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    private static StructDeclarationNode MakeStructWithPublicFields(
        string structName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new StructDeclarationNode { Name = Tok(structName), Members = members };
    }

    private static TraitDeclarationNode MakeTraitWithFields(
        string traitName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    private static WithDeclarationNode MakeWithDeclaration(
        string typeName, string traitName, params (string source, string target)[] mappings)
    {
        return new WithDeclarationNode
        {
            TypeName = Tok(typeName),
            TraitName = Tok(traitName),
            Mappings = mappings.Select(m => new MemberMappingNode
            {
                SourceName = Tok(m.source),
                TargetName = Tok(m.target),
            }).ToImmutableArray(),
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TopLevelItemNode[] items) =>
        new() { Items = items.ToImmutableArray() };

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] StructNames =
        ["Point", "Vector", "Rect", "Size", "Color"];

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    /// <summary>Field names shared between type and trait for structural matching.</summary>
    private static readonly string[] SharedFieldNames =
        ["value", "count", "size", "width", "height"];

    /// <summary>Source field names on the type — deliberately different from trait field names.</summary>
    private static readonly string[] SourceFieldNames =
        ["myValue", "myCount", "mySize", "myWidth", "myHeight"];

    /// <summary>Whether the implementing type is a class or struct.</summary>
    public enum ImplTypeKind { Class, Struct }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// A scenario where a type structurally satisfies a trait (same field names and types)
    /// and is assigned to a trait-typed variable.
    /// </summary>
    public record StructuralSatisfactionScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string FieldType, string FieldName)> Fields)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} structurally satisfies trait {TraitName} {{ {fields} }}";
        }
    }

    /// <summary>
    /// A scenario where a type satisfies a trait via a with-block (different field names,
    /// mapped via with) and is assigned to a trait-typed variable.
    /// </summary>
    public record WithBlockSatisfactionScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string FieldType, string SourceName, string TraitFieldName)> Mappings)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var maps = string.Join(", ", Mappings.Select(m => $"{m.SourceName} as {m.TraitFieldName}"));
            return $"{kind} {TypeName} satisfies trait {TraitName} via with {{ {maps} }}";
        }
    }

    /// <summary>
    /// A scenario where a type does NOT satisfy a trait (missing fields) and is
    /// assigned to a trait-typed variable, expecting a MIST_TRAIT_UNSATISFIED diagnostic.
    /// </summary>
    public record UnsatisfiedScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string FieldType, string FieldName)> TypeFields,
        ImmutableArray<(string FieldType, string FieldName)> TraitFields)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var typeF = string.Join(", ", TypeFields.Select(f => f.FieldName));
            var traitF = string.Join(", ", TraitFields.Select(f => f.FieldName));
            return $"{kind} {TypeName} [{typeF}] does NOT satisfy trait {TraitName} [{traitF}]";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where a type structurally satisfies a trait.
    /// Both type and trait have the same field names and types.
    /// </summary>
    private static Gen<StructuralSatisfactionScenario> GenStructuralSatisfactionScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let fieldNames = SharedFieldNames.Take(fieldCount).ToArray()
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], fieldNames[i]))
                   .ToImmutableArray()
               select new StructuralSatisfactionScenario(typeName, traitName, typeKind, fields);
    }

    /// <summary>
    /// Generates a scenario where a type satisfies a trait via a with-block.
    /// The type's field names are deliberately different from the trait's.
    /// </summary>
    private static Gen<WithBlockSatisfactionScenario> GenWithBlockSatisfactionScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let sourceNames = SourceFieldNames.Take(fieldCount).ToArray()
               let traitFieldNames = SharedFieldNames.Take(fieldCount).ToArray()
               let mappings = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], sourceNames[i], traitFieldNames[i]))
                   .ToImmutableArray()
               select new WithBlockSatisfactionScenario(typeName, traitName, typeKind, mappings);
    }

    /// <summary>
    /// Generates a scenario where a type does NOT satisfy a trait.
    /// The type has some fields but the trait requires different field names,
    /// and there is no with-block to bridge the gap.
    /// </summary>
    private static Gen<UnsatisfiedScenario> GenUnsatisfiedScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from traitFieldCount in Gen.Choose(1, 3)
               from traitFieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(traitFieldCount)
               // Trait uses SharedFieldNames
               let traitFields = Enumerable.Range(0, traitFieldCount)
                   .Select(i => (traitFieldTypes[i], SharedFieldNames[i]))
                   .ToImmutableArray()
               // Type uses SourceFieldNames (different names, no structural match)
               from typeFieldCount in Gen.Choose(0, 2)
               from typeFieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(Math.Max(typeFieldCount, 1))
               let typeFields = Enumerable.Range(0, typeFieldCount)
                   .Select(i => (typeFieldTypes[i], SourceFieldNames[i]))
                   .ToImmutableArray()
               select new UnsatisfiedScenario(typeName, traitName, typeKind, typeFields, traitFields);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<StructuralSatisfactionScenario> ArbitraryStructuralSatisfaction() =>
        GenStructuralSatisfactionScenario().ToArbitrary();

    public static Arbitrary<WithBlockSatisfactionScenario> ArbitraryWithBlockSatisfaction() =>
        GenWithBlockSatisfactionScenario().ToArbitrary();

    public static Arbitrary<UnsatisfiedScenario> ArbitraryUnsatisfied() =>
        GenUnsatisfiedScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a CompilationUnitNode for a structural satisfaction scenario:
    /// - A trait with field members
    /// - A class/struct with matching public fields
    /// - A Program class with Main: TraitName x = new TypeName();
    /// </summary>
    private static CompilationUnitNode BuildStructuralSatisfactionAst(StructuralSatisfactionScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Type declaration with matching public fields
        if (scenario.TypeKind == ImplTypeKind.Class)
        {
            var classDecl = MakeClassWithPublicFields(
                scenario.TypeName,
                scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
            items.Add(MakeClassTypeItem(classDecl));
        }
        else
        {
            var structDecl = MakeStructWithPublicFields(
                scenario.TypeName,
                scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
            items.Add(MakeStructTypeItem(structDecl));
        }

        // 3. Program class: TraitName x = new TypeName();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.TypeName));
        items.Add(MakeClassTypeItem(MakeProgramClass(mainStatements)));

        return MakeCompilationUnit(items.ToArray());
    }

    /// <summary>
    /// Builds a CompilationUnitNode for a with-block satisfaction scenario:
    /// - A trait with field members
    /// - A class/struct with differently-named public fields
    /// - A with-block mapping source fields to trait fields
    /// - A Program class with Main: TraitName x = new TypeName();
    /// </summary>
    private static CompilationUnitNode BuildWithBlockSatisfactionAst(WithBlockSatisfactionScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.Mappings.Select(m => (m.FieldType, m.TraitFieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Type declaration with differently-named public fields
        if (scenario.TypeKind == ImplTypeKind.Class)
        {
            var classDecl = MakeClassWithPublicFields(
                scenario.TypeName,
                scenario.Mappings.Select(m => (m.FieldType, m.SourceName)).ToArray());
            items.Add(MakeClassTypeItem(classDecl));
        }
        else
        {
            var structDecl = MakeStructWithPublicFields(
                scenario.TypeName,
                scenario.Mappings.Select(m => (m.FieldType, m.SourceName)).ToArray());
            items.Add(MakeStructTypeItem(structDecl));
        }

        // 3. With-block mapping source names to trait field names
        var withDecl = MakeWithDeclaration(
            scenario.TypeName,
            scenario.TraitName,
            scenario.Mappings.Select(m => (m.SourceName, m.TraitFieldName)).ToArray());
        items.Add(MakeWithItem(withDecl));

        // 4. Program class: TraitName x = new TypeName();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.TypeName));
        items.Add(MakeClassTypeItem(MakeProgramClass(mainStatements)));

        return MakeCompilationUnit(items.ToArray());
    }

    /// <summary>
    /// Builds a CompilationUnitNode for an unsatisfied scenario:
    /// - A trait with field members
    /// - A class/struct with differently-named fields (no structural match, no with-block)
    /// - A Program class with Main: TraitName x = new TypeName();
    /// </summary>
    private static CompilationUnitNode BuildUnsatisfiedAst(UnsatisfiedScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.TraitFields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Type declaration with non-matching fields (or no fields)
        if (scenario.TypeKind == ImplTypeKind.Class)
        {
            var classDecl = scenario.TypeFields.Length > 0
                ? MakeClassWithPublicFields(
                    scenario.TypeName,
                    scenario.TypeFields.Select(f => (f.FieldType, f.FieldName)).ToArray())
                : new ClassDeclarationNode
                {
                    Name = Tok(scenario.TypeName),
                    Members = ImmutableArray<MemberDeclarationNode>.Empty,
                };
            items.Add(MakeClassTypeItem(classDecl));
        }
        else
        {
            var structDecl = scenario.TypeFields.Length > 0
                ? MakeStructWithPublicFields(
                    scenario.TypeName,
                    scenario.TypeFields.Select(f => (f.FieldType, f.FieldName)).ToArray())
                : new StructDeclarationNode
                {
                    Name = Tok(scenario.TypeName),
                    Members = ImmutableArray<MemberDeclarationNode>.Empty,
                };
            items.Add(MakeStructTypeItem(structDecl));
        }

        // 3. Program class: TraitName x = new TypeName();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.TypeName));
        items.Add(MakeClassTypeItem(MakeProgramClass(mainStatements)));

        return MakeCompilationUnit(items.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6a: For any type that structurally satisfies a trait (matching public
    /// fields by name and type), assigning an instance of that type to a trait-typed
    /// variable SHALL NOT produce a MIST_TRAIT_UNSATISFIED diagnostic.
    ///
    /// **Validates: Requirements 7.1, 7.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TraitAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool StructuralSatisfaction_AssignmentIsValid(StructuralSatisfactionScenario scenario)
    {
        var ast = BuildStructuralSatisfactionAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // No MIST_TRAIT_UNSATISFIED diagnostic should be emitted
        var traitUnsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED")
            .ToList();

        return traitUnsatisfiedDiags.Count == 0;
    }

    /// <summary>
    /// Property 6b: For any type that satisfies a trait via a with-block (mapped fields),
    /// assigning an instance of that type to a trait-typed variable SHALL NOT produce
    /// a MIST_TRAIT_UNSATISFIED diagnostic.
    ///
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TraitAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool WithBlockSatisfaction_AssignmentIsValid(WithBlockSatisfactionScenario scenario)
    {
        var ast = BuildWithBlockSatisfactionAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // No MIST_TRAIT_UNSATISFIED diagnostic should be emitted
        var traitUnsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED")
            .ToList();

        return traitUnsatisfiedDiags.Count == 0;
    }

    /// <summary>
    /// Property 6c: For any type that does NOT satisfy a trait (no structural match
    /// and no with-block), assigning an instance of that type to a trait-typed variable
    /// SHALL produce a MIST_TRAIT_UNSATISFIED diagnostic naming the trait and the type.
    ///
    /// **Validates: Requirements 7.3, 7.4, 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TraitAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool UnsatisfiedType_AssignmentProducesDiagnostic(UnsatisfiedScenario scenario)
    {
        var ast = BuildUnsatisfiedAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // A MIST_TRAIT_UNSATISFIED diagnostic should be emitted mentioning both the type and trait
        var traitUnsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                        && d.Message.Contains(scenario.TypeName)
                        && d.Message.Contains(scenario.TraitName))
            .ToList();

        return traitUnsatisfiedDiags.Count > 0;
    }
}
