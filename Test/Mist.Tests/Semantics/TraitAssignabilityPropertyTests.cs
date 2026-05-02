// Feature: traits, Property 6: Trait assignability
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

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

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            "string" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            },
            "bool" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            },
            _ => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };
    }

    // ─── Expression / statement builders ─────────────────────────────────

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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
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

    /// <summary>
    /// Creates: TraitName varName = new ClassName();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
                },
                Name = Tok(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        ImmutableArray<StatementNode>? bodyStatements = null,
        bool isStatic = false,
        ParameterListNode? parameters = null,
        TypeReferenceNode? returnType = null)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if (isStatic)
            modifiers.Add(new StaticModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
                },
            },
            Modifiers = modifiers.ToImmutableArray(),
        };
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    // ─── AST construction helpers ────────────────────────────────────────

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

    private static TopLevelItemNode MakeClassTypeItem(ClassDeclarationNode classDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        };

    private static TopLevelItemNode MakeTraitTypeItem(TraitDeclarationNode traitDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        };

    private static TopLevelItemNode MakeWithItem(WithDeclarationNode withDecl) =>
        new WithItemTopLevelItemNode { WithDeclaration = withDecl };

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    /// <summary>
    /// Source field names on the type — deliberately different from trait field names
    /// so that structural matching by name does NOT apply.
    /// </summary>
    private static readonly string[] SourceFieldNames =
        ["myValue", "myCount", "mySize", "myWidth", "myHeight"];

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// A scenario where a type structurally satisfies a trait and is assigned
    /// to a trait-typed variable.
    /// </summary>
    public record StructuralAssignScenario(
        string ClassName,
        string TraitName,
        ImmutableArray<(string FieldType, string FieldName)> Fields)
    {
        public override string ToString()
        {
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"class {ClassName} {{ {fields} }} assigned to {TraitName}";
        }
    }

    /// <summary>
    /// A scenario where a type satisfies a trait via a with-block and is assigned
    /// to a trait-typed variable.
    /// </summary>
    public record WithBlockAssignScenario(
        string ClassName,
        string TraitName,
        ImmutableArray<(string FieldType, string SourceName, string TraitFieldName)> Mappings)
    {
        public override string ToString()
        {
            var maps = string.Join(", ", Mappings.Select(m => $"{m.SourceName} as {m.TraitFieldName}"));
            return $"class {ClassName} assigned to {TraitName} via with {{ {maps} }}";
        }
    }

    /// <summary>
    /// A scenario where a type does NOT satisfy a trait (missing members)
    /// and is assigned to a trait-typed variable.
    /// </summary>
    public record UnsatisfiedAssignScenario(
        string ClassName,
        string TraitName,
        ImmutableArray<(string FieldType, string FieldName)> ProvidedFields,
        ImmutableArray<(string FieldType, string FieldName)> MissingFields)
    {
        public override string ToString()
        {
            var provided = string.Join(", ", ProvidedFields.Select(f => $"{f.FieldType} {f.FieldName}"));
            var missing = string.Join(", ", MissingFields.Select(f => f.FieldName));
            return $"class {ClassName} [has: {provided}] [missing: {missing}] assigned to {TraitName}";
        }
    }

    /// <summary>
    /// A scenario where a type satisfies a trait and is passed as a method parameter
    /// typed as the trait.
    /// </summary>
    public record ParameterPassingScenario(
        string ClassName,
        string TraitName,
        ImmutableArray<(string FieldType, string FieldName)> Fields)
    {
        public override string ToString()
        {
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"class {ClassName} {{ {fields} }} passed as {TraitName} parameter";
        }
    }

    /// <summary>
    /// A scenario where a type satisfies a trait and is returned from a method
    /// with a trait return type.
    /// </summary>
    public record ReturnTypeScenario(
        string ClassName,
        string TraitName,
        ImmutableArray<(string FieldType, string FieldName)> Fields)
    {
        public override string ToString()
        {
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"class {ClassName} {{ {fields} }} returned as {TraitName}";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<StructuralAssignScenario> GenStructuralAssignScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let fieldNames = FieldNames.Take(fieldCount).ToArray()
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], fieldNames[i]))
                   .ToImmutableArray()
               select new StructuralAssignScenario(className, traitName, fields);
    }

    private static Gen<WithBlockAssignScenario> GenWithBlockAssignScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let sourceNames = SourceFieldNames.Take(fieldCount).ToArray()
               let traitFieldNames = FieldNames.Take(fieldCount).ToArray()
               let mappings = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], sourceNames[i], traitFieldNames[i]))
                   .ToImmutableArray()
               select new WithBlockAssignScenario(className, traitName, mappings);
    }

    private static Gen<UnsatisfiedAssignScenario> GenUnsatisfiedAssignScenario()
    {
        // The class always has at least one field but is missing at least one trait field.
        // This ensures the class type resolves properly while still not satisfying the trait.
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from totalCount in Gen.Choose(2, 4)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(totalCount)
               let fieldNames = FieldNames.Take(totalCount).ToArray()
               from providedCount in Gen.Choose(1, totalCount - 1)
               let provided = Enumerable.Range(0, providedCount)
                   .Select(i => (fieldTypes[i], fieldNames[i]))
                   .ToImmutableArray()
               let missing = Enumerable.Range(providedCount, totalCount - providedCount)
                   .Select(i => (fieldTypes[i], fieldNames[i]))
                   .ToImmutableArray()
               select new UnsatisfiedAssignScenario(className, traitName, provided, missing);
    }

    private static Gen<ParameterPassingScenario> GenParameterPassingScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let fieldNames = FieldNames.Take(fieldCount).ToArray()
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], fieldNames[i]))
                   .ToImmutableArray()
               select new ParameterPassingScenario(className, traitName, fields);
    }

    private static Gen<ReturnTypeScenario> GenReturnTypeScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let fieldNames = FieldNames.Take(fieldCount).ToArray()
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], fieldNames[i]))
                   .ToImmutableArray()
               select new ReturnTypeScenario(className, traitName, fields);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction for each scenario
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a program where a structurally satisfying type is assigned to a
    /// trait-typed variable:
    ///   trait T { int value; }
    ///   class C { public int value; }
    ///   class Program { static void Main() { T x = new C(); } }
    /// </summary>
    private static CompilationUnitNode BuildStructuralAssignAst(StructuralAssignScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Class that structurally satisfies the trait
        var classDecl = MakeClassWithPublicFields(
            scenario.ClassName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeClassTypeItem(classDecl));

        // 3. Program class with Main: TraitName x = new ClassName();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.ClassName));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };
        items.Add(MakeClassTypeItem(programClass));

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    /// <summary>
    /// Builds a program where a type satisfies a trait via with-block and is
    /// assigned to a trait-typed variable:
    ///   trait T { int value; }
    ///   class C { public int myValue; }
    ///   with C : T { myValue as value; }
    ///   class Program { static void Main() { T x = new C(); } }
    /// </summary>
    private static CompilationUnitNode BuildWithBlockAssignAst(WithBlockAssignScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.Mappings.Select(m => (m.FieldType, m.TraitFieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Class with differently-named fields
        var classDecl = MakeClassWithPublicFields(
            scenario.ClassName,
            scenario.Mappings.Select(m => (m.FieldType, m.SourceName)).ToArray());
        items.Add(MakeClassTypeItem(classDecl));

        // 3. With-block mapping source names to trait field names
        var withDecl = MakeWithDeclaration(
            scenario.ClassName,
            scenario.TraitName,
            scenario.Mappings.Select(m => (m.SourceName, m.TraitFieldName)).ToArray());
        items.Add(MakeWithItem(withDecl));

        // 4. Program class with Main: TraitName x = new ClassName();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.ClassName));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };
        items.Add(MakeClassTypeItem(programClass));

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    /// <summary>
    /// Builds a program where a type does NOT satisfy a trait and is assigned
    /// to a trait-typed variable:
    ///   trait T { int value; int count; }
    ///   class C { public int value; }  // missing 'count'
    ///   class Program { static void Main() { T x = new C(); } }
    /// </summary>
    private static CompilationUnitNode BuildUnsatisfiedAssignAst(UnsatisfiedAssignScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait with ALL fields (provided + missing)
        var allFields = scenario.ProvidedFields.Concat(scenario.MissingFields).ToArray();
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            allFields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Class with only the provided fields (missing some trait fields)
        var classDecl = MakeClassWithPublicFields(
            scenario.ClassName,
            scenario.ProvidedFields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeClassTypeItem(classDecl));

        // 3. Program class with Main: TraitName x = new ClassName();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.ClassName));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };
        items.Add(MakeClassTypeItem(programClass));

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    /// <summary>
    /// Builds a program where a structurally satisfying type is passed as a
    /// method parameter typed as the trait:
    ///   trait T { int value; }
    ///   class C { public int value; }
    ///   class Program {
    ///     static void Accept(T t) { }
    ///     static void Main() { Accept(new C()); }
    ///   }
    /// </summary>
    private static CompilationUnitNode BuildParameterPassingAst(ParameterPassingScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Class that structurally satisfies the trait
        var classDecl = MakeClassWithPublicFields(
            scenario.ClassName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeClassTypeItem(classDecl));

        // 3. Program class with Accept(T t) and Main calling Accept(new C())
        var acceptParam = new ParameterListNode
        {
            Parameter = new ParameterNode
            {
                Type = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(scenario.TraitName),
                        Span = new Storm.Runtime.SourceSpan(0, scenario.TraitName.Length),
                    },
                },
                Name = Tok("t"),
            },
        };
        var acceptMethod = MakeMethodDecl("Accept", isStatic: true, parameters: acceptParam);

        // Main: Accept(new C());
        var callExpr = new InvocationSuffixPostfixSuffixNode
        {
            Arguments = new ArgumentListNode
            {
                Expression = MakeNewObjectExpression(scenario.ClassName),
            },
        };
        var acceptCall = new PostfixNode
        {
            Target = new IdentifierExpressionPrimaryNode { Name = Tok("Accept") },
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(callExpr),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = acceptCall };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        var callStatement = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = new ExpressionNode { Assignment = assign },
            },
        };

        var mainMethod = MakeMethodDecl("Main",
            ImmutableArray.Create<StatementNode>(callStatement), isStatic: true);

        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                WrapMethod(acceptMethod),
                WrapMethod(mainMethod)),
        };
        items.Add(MakeClassTypeItem(programClass));

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    /// <summary>
    /// Builds a program where a structurally satisfying type is returned from
    /// a method with a trait return type:
    ///   trait T { int value; }
    ///   class C { public int value; }
    ///   class Program {
    ///     static T Create() { return new C(); }
    ///     static void Main() { T x = Create(); }
    ///   }
    /// </summary>
    private static CompilationUnitNode BuildReturnTypeAst(ReturnTypeScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = MakeTraitWithFields(
            scenario.TraitName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeTraitTypeItem(traitDecl));

        // 2. Class that structurally satisfies the trait
        var classDecl = MakeClassWithPublicFields(
            scenario.ClassName,
            scenario.Fields.Select(f => (f.FieldType, f.FieldName)).ToArray());
        items.Add(MakeClassTypeItem(classDecl));

        // 3. Program class with Create() returning trait type and Main calling it
        // Create method: static T Create() { return new C(); }
        var returnStatement = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = MakeNewObjectExpression(scenario.ClassName),
            },
        };

        var createMethod = MakeMethodDecl("Create",
            ImmutableArray.Create<StatementNode>(returnStatement),
            isStatic: true,
            returnType: new NamedTypeRefTypeReferenceNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = Tok(scenario.TraitName),
                    Span = new Storm.Runtime.SourceSpan(0, scenario.TraitName.Length),
                },
            });

        // Main: T x = Create();
        var createCallPrimary = new IdentifierExpressionPrimaryNode { Name = Tok("Create") };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var createCallPostfix = new PostfixNode
        {
            Target = createCallPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var createUnary = new PostfixExpressionUnaryNode { Primary = createCallPostfix };
        var createMult = new MultiplicativeNode { Left = createUnary };
        var createAdd = new AdditiveNode { Left = createMult };
        var createRel = new RelationalNode { Left = createAdd };
        var createEq = new EqualityNode { Left = createRel };
        var createLogAnd = new LogicalAndNode { Left = createEq };
        var createLogOr = new LogicalOrNode { Left = createLogAnd };
        var createAssign = new AssignmentNode { Target = createLogOr };
        var createExpr = new ExpressionNode { Assignment = createAssign };

        var mainVarDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(scenario.TraitName),
                        Span = new Storm.Runtime.SourceSpan(0, scenario.TraitName.Length),
                    },
                },
                Name = Tok("x"),
                Initializer = createExpr,
            },
        };

        var mainMethod = MakeMethodDecl("Main",
            ImmutableArray.Create<StatementNode>(mainVarDecl), isStatic: true);

        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                WrapMethod(createMethod),
                WrapMethod(mainMethod)),
        };
        items.Add(MakeClassTypeItem(programClass));

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6a: For any type that structurally satisfies a trait, assigning
    /// an instance of that type to a trait-typed variable SHALL NOT produce a
    /// MIST_TRAIT_UNSATISFIED diagnostic.
    ///
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Fact]
    public void StructuralSatisfaction_AssignmentIsValid()
    {
        var property = Prop.ForAll(
            GenStructuralAssignScenario().ToArbitrary(),
            scenario =>
            {
                var ast = BuildStructuralAssignAst(scenario);
                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var traitUnsatisfiedDiags = diagnostics
                    .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                                && d.Message.Contains(scenario.ClassName)
                                && d.Message.Contains(scenario.TraitName))
                    .ToList();

                return (traitUnsatisfiedDiags.Count == 0)
                    .ToProperty()
                    .Label($"Expected no MIST_TRAIT_UNSATISFIED for {scenario.ClassName} -> {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 6b: For any type that satisfies a trait via a with-block, assigning
    /// an instance of that type to a trait-typed variable SHALL NOT produce a
    /// MIST_TRAIT_UNSATISFIED diagnostic.
    ///
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Fact]
    public void WithBlockSatisfaction_AssignmentIsValid()
    {
        var property = Prop.ForAll(
            GenWithBlockAssignScenario().ToArbitrary(),
            scenario =>
            {
                var ast = BuildWithBlockAssignAst(scenario);
                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var traitUnsatisfiedDiags = diagnostics
                    .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                                && d.Message.Contains(scenario.ClassName)
                                && d.Message.Contains(scenario.TraitName))
                    .ToList();

                return (traitUnsatisfiedDiags.Count == 0)
                    .ToProperty()
                    .Label($"Expected no MIST_TRAIT_UNSATISFIED for {scenario.ClassName} -> {scenario.TraitName} via with-block");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 6c: For any type that does NOT satisfy a trait (missing members),
    /// assigning an instance of that type to a trait-typed variable SHALL produce
    /// a MIST_TRAIT_UNSATISFIED diagnostic naming the trait and the type.
    ///
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Fact]
    public void UnsatisfiedType_AssignmentEmitsDiagnostic()
    {
        var property = Prop.ForAll(
            GenUnsatisfiedAssignScenario().ToArbitrary(),
            scenario =>
            {
                var ast = BuildUnsatisfiedAssignAst(scenario);
                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var traitUnsatisfiedDiags = diagnostics
                    .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                                && d.Message.Contains(scenario.ClassName)
                                && d.Message.Contains(scenario.TraitName))
                    .ToList();

                return (traitUnsatisfiedDiags.Count >= 1)
                    .ToProperty()
                    .Label($"Expected MIST_TRAIT_UNSATISFIED for {scenario.ClassName} -> {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 6d: For any type that structurally satisfies a trait, passing an
    /// instance of that type as a method parameter typed as the trait SHALL NOT
    /// produce a MIST_TRAIT_UNSATISFIED diagnostic.
    ///
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Fact]
    public void StructuralSatisfaction_ParameterPassingIsValid()
    {
        var property = Prop.ForAll(
            GenParameterPassingScenario().ToArbitrary(),
            scenario =>
            {
                var ast = BuildParameterPassingAst(scenario);
                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var traitUnsatisfiedDiags = diagnostics
                    .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                                && d.Message.Contains(scenario.ClassName)
                                && d.Message.Contains(scenario.TraitName))
                    .ToList();

                return (traitUnsatisfiedDiags.Count == 0)
                    .ToProperty()
                    .Label($"Expected no MIST_TRAIT_UNSATISFIED for parameter passing {scenario.ClassName} -> {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 6e: For any type that structurally satisfies a trait, returning an
    /// instance of that type from a method with a trait return type SHALL NOT
    /// produce a MIST_TRAIT_UNSATISFIED diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Fact]
    public void StructuralSatisfaction_ReturnTypeIsValid()
    {
        var property = Prop.ForAll(
            GenReturnTypeScenario().ToArbitrary(),
            scenario =>
            {
                var ast = BuildReturnTypeAst(scenario);
                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var traitUnsatisfiedDiags = diagnostics
                    .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                                && d.Message.Contains(scenario.ClassName)
                                && d.Message.Contains(scenario.TraitName))
                    .ToList();

                return (traitUnsatisfiedDiags.Count == 0)
                    .ToProperty()
                    .Label($"Expected no MIST_TRAIT_UNSATISFIED for return type {scenario.ClassName} -> {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }
}
