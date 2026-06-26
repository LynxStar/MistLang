// Feature: default-keyword
// Property 14: C# emission of default value accessors — **Validates: Requirements 12.1**
// Property 15: C# emission of default expressions — **Validates: Requirements 12.3**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for C# emission of default value constructs.
///
/// Property 14: C# emission of default value accessors
/// For any defaultable type (class or struct), the C# emitter SHALL produce a static
/// factory method named <c>__CreateDefault</c> on the emitted C# type that returns a
/// new instance with the computed default values on each call.
///
/// **Validates: Requirements 12.1**
///
/// Property 15: C# emission of default expressions
/// For any <c>default(TypeName)</c> expression or bare <c>default</c> literal expression
/// targeting a defaultable type (class or struct), the C# emitter SHALL emit a call to
/// the corresponding type's <c>__CreateDefault()</c> factory method.
///
/// **Validates: Requirements 12.3**
/// </summary>
public sealed partial class DefaultEmitterPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public DefaultEmitterPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistDefaultEmitterTests_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", diagnostics);

        // Read all .cs files and concatenate (excluding .csproj and MistVoid.g.cs)
        var csFiles = Directory.GetFiles(outputDir, "*.cs")
            .Where(f => !f.EndsWith("MistVoid.g.cs", StringComparison.OrdinalIgnoreCase));
        var sb = new System.Text.StringBuilder();
        foreach(var file in csFiles)
        {
            _ = sb.AppendLine(File.ReadAllText(file));
        }
        return sb.ToString();
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir))
            {
                File.Delete(file);
            }
        }
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static PrimitiveTypeNode MakePrimitiveType(string typeName) => typeName switch
    {
        "int" => new IntTypePrimitiveTypeNode(),
        "bool" => new BoolTypePrimitiveTypeNode(),
        "string" => new StringTypePrimitiveTypeNode(),
        "double" => new DoubleTypePrimitiveTypeNode(),
        "float" => new FloatTypePrimitiveTypeNode(),
        "char" => new CharTypePrimitiveTypeNode(),
        "long" => new LongTypePrimitiveTypeNode(),
        "short" => new ShortTypePrimitiveTypeNode(),
        "byte" => new ByteTypePrimitiveTypeNode(),
        _ => new IntTypePrimitiveTypeNode(),
    };

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = MakePrimitiveType(typeName),
            }
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string typeName)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(typeName) },
            }
        };
    }

    private static FieldDeclarationNode MakeField(
        string fieldName, TypeReferenceNode typeRef, ExpressionNode? initializer = null)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = typeRef,
            Initializer = initializer,
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static MemberDeclarationNode MakeFieldMember(FieldDeclarationNode field)
    {
        return new FieldMemberMemberDeclarationNode { Field = field };
    }

    private static MemberDeclarationNode MakeDefaultBlockMember(
        ImmutableArray<DefaultFieldInitializerNode> entries)
    {
        return new DefaultBlockMemberMemberDeclarationNode
        {
            DefaultBlock = new DefaultBlockDeclarationNode { Entries = entries },
        };
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        ImmutableArray<MemberDeclarationNode> members)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        ImmutableArray<MemberDeclarationNode> members)
    {
        return new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode classDecl)
    {
        return new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };
    }

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode structDecl)
    {
        return new StructTypeTypeDeclarationNode { StructDeclaration = structDecl };
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ─── Expression helpers ──────────────────────────────────────────────

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
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIntLiteralExpression(string value)
    {
        return WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        });
    }

    /// <summary>
    /// Creates a Main method with a body containing the given statements.
    /// </summary>
    private static MemberDeclarationNode MakeMainMethod(
        ImmutableArray<StatementNode> statements)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
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
        return new MethodMemberMemberDeclarationNode { Method = mainMethod };
    }

    /// <summary>
    /// Creates a typed variable declaration statement: <c>TypeName varName = expr;</c>
    /// </summary>
    private static StatementNode MakeTypedVarDeclStatement(
        string typeName, string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(typeName),
                Name = MakeToken(varName),
                Initializer = initializer,
            },
        };
    }

    // ─── Regex patterns ──────────────────────────────────────────────────

    /// <summary>
    /// Matches a <c>public static TypeName __CreateDefault()</c> method declaration.
    /// Captures the type name.
    /// </summary>
    [GeneratedRegex(@"public\s+static\s+(\w+)\s+__CreateDefault\s*\(\s*\)", RegexOptions.Multiline)]
    private static partial Regex CreateDefaultMethodPattern();

    /// <summary>
    /// Matches a <c>TypeName.__CreateDefault()</c> call site.
    /// Captures the type name.
    /// </summary>
    [GeneratedRegex(@"(\w+)\.__CreateDefault\s*\(\s*\)", RegexOptions.Multiline)]
    private static partial Regex CreateDefaultCallPattern();

    /// <summary>
    /// Matches <c>var inst = new TypeName();</c> inside a __CreateDefault method body.
    /// </summary>
    [GeneratedRegex(@"var\s+inst\s*=\s*new\s+(\w+)\s*\(\s*\)\s*;", RegexOptions.Multiline)]
    private static partial Regex NewInstancePattern();

    /// <summary>
    /// Matches <c>inst.fieldName = value;</c> field assignment inside a __CreateDefault method body.
    /// </summary>
    [GeneratedRegex(@"inst\.(\w+)\s*=\s*(.+?)\s*;", RegexOptions.Multiline)]
    private static partial Regex FieldAssignmentPattern();

    /// <summary>
    /// Matches <c>this.fieldName = value;</c> field assignment inside a constructor body.
    /// </summary>
    [GeneratedRegex(@"this\.(\w+)\s*=\s*(.+?)\s*;", RegexOptions.Multiline)]
    private static partial Regex CtorFieldAssignmentPattern();

    // ─── Scenario records ────────────────────────────────────────────────

    /// <summary>
    /// Scenario for Property 14: a defaultable type with primitive fields.
    /// </summary>
#pragma warning disable CA1034
    public record CreateDefaultScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields} }}";
        }
    }

    /// <summary>
    /// Scenario for Property 15: a program with a default expression targeting a
    /// defaultable type.
    /// </summary>
    public record DefaultExpressionScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields} }}, default({TypeName})";
        }
    }
#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "double", "float", "char", "long", "short", "byte"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon"];

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a defaultable type with 1–4 primitive fields.
    /// </summary>
    private static Gen<CreateDefaultScenario> GenCreateDefaultScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenPrimitiveFields(fieldCount)
               select new CreateDefaultScenario(isStruct, typeName, fields);
    }

    /// <summary>
    /// Generates a program with a defaultable type and a default expression.
    /// </summary>
    private static Gen<DefaultExpressionScenario> GenDefaultExpressionScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 3)
               from fields in GenPrimitiveFields(fieldCount)
               select new DefaultExpressionScenario(isStruct, typeName, fields);
    }

    private static Gen<ImmutableArray<(string FieldName, string FieldType)>> GenPrimitiveFields(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(ImmutableArray<(string, string)>.Empty);
        }

        Gen<ImmutableArray<(string, string)>> result =
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

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<CreateDefaultScenario> ArbitraryCreateDefaultScenario() =>
        GenCreateDefaultScenario().ToArbitrary();

    public static Arbitrary<DefaultExpressionScenario> ArbitraryDefaultExpressionScenario() =>
        GenDefaultExpressionScenario().ToArbitrary();

    // ─── AST builders ────────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST for Property 14: a defaultable type with primitive fields
    /// and a Program class with a Main entry point.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildCreateDefaultAst(
        CreateDefaultScenario scenario)
    {
        var members = scenario.Fields
            .Select(f => MakeFieldMember(MakeField(f.FieldName, MakePrimitiveTypeRef(f.FieldType))))
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        // Add Program class with Main entry point
        var programClass = MakeClassDecl(
            "Program",
            ImmutableArray.Create(MakeMainMethod(ImmutableArray<StatementNode>.Empty)));

        var ast = MakeCompilationUnit(typeDecl, WrapClass(programClass));

        // Build matching source text
        var sb = new System.Text.StringBuilder();
        var kind = scenario.IsStruct ? "struct" : "class";
        _ = sb.AppendLine($"{kind} {scenario.TypeName} {{");
        foreach(var (fieldName, fieldType) in scenario.Fields)
        {
            _ = sb.AppendLine($"    {fieldType} {fieldName};");
        }
        _ = sb.AppendLine("}");
        _ = sb.AppendLine("class Program { public static void Main() { } }");

        return (ast, sb.ToString());
    }

    /// <summary>
    /// Builds an AST for Property 15: a defaultable type with a default(TypeName)
    /// expression in Main, plus a Program class with Main entry point.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildDefaultExpressionAst(
        DefaultExpressionScenario scenario)
    {
        var members = scenario.Fields
            .Select(f => MakeFieldMember(MakeField(f.FieldName, MakePrimitiveTypeRef(f.FieldType))))
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        // Build a default(TypeName) expression
        var defaultExpr = WrapPrimaryAsExpression(
            new DefaultExpressionPrimaryNode
            {
                Type = MakeNamedTypeRef(scenario.TypeName),
            });

        // Create: TypeName x = default(TypeName);
        var varDeclStmt = MakeTypedVarDeclStatement(scenario.TypeName, "x", defaultExpr);

        var programClass = MakeClassDecl(
            "Program",
            ImmutableArray.Create(MakeMainMethod(ImmutableArray.Create(varDeclStmt))));

        var ast = MakeCompilationUnit(typeDecl, WrapClass(programClass));

        // Build matching source text
        var sb = new System.Text.StringBuilder();
        var kind = scenario.IsStruct ? "struct" : "class";
        _ = sb.AppendLine($"{kind} {scenario.TypeName} {{");
        foreach(var (fieldName, fieldType) in scenario.Fields)
        {
            _ = sb.AppendLine($"    {fieldType} {fieldName};");
        }
        _ = sb.AppendLine("}");
        _ = sb.AppendLine($"class Program {{ public static void Main() {{ {scenario.TypeName} x = default({scenario.TypeName}); }} }}");

        return (ast, sb.ToString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 14: C# emission of default value accessors
    //
    // **Validates: Requirements 12.1**
    //
    // For any defaultable type (class or struct), the C# emitter SHALL
    // produce a static factory method named __CreateDefault on the emitted
    // C# type that returns a new instance with the computed default values
    // on each call.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 50,
        Arbitrary = new[] { typeof(DefaultEmitterPropertyTests) })]
    public bool CreateDefault_FactoryMethod_EmittedForDefaultableTypes(CreateDefaultScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildCreateDefaultAst(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false;
        }

        // 1. Verify __CreateDefault() method declaration exists for the type
        var methodMatches = CreateDefaultMethodPattern().Matches(csContent);
        var hasFactoryMethod = methodMatches
            .Cast<Match>()
            .Any(m => m.Groups[1].Value == scenario.TypeName);

        if(!hasFactoryMethod)
        {
            return false;
        }

        // 2. Verify the factory method body creates a new instance
        var newInstanceMatches = NewInstancePattern().Matches(csContent);
        var hasNewInstance = newInstanceMatches
            .Cast<Match>()
            .Any(m => m.Groups[1].Value == scenario.TypeName);

        if(!hasNewInstance)
        {
            return false;
        }

        // 3. Verify each field is assigned somewhere in the emitted C# code.
        //    For structs, field assignments appear in __CreateDefault() as inst.field = value.
        //    For classes, field assignments may appear in the protected parameterless
        //    constructor as this.field = value (when the emitter generates the constructor).
        var fieldAssignments = FieldAssignmentPattern().Matches(csContent);
        var ctorFieldAssignments = CtorFieldAssignmentPattern().Matches(csContent);
        var assignedFields = fieldAssignments
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Concat(ctorFieldAssignments.Cast<Match>().Select(m => m.Groups[1].Value))
            .ToHashSet();

        foreach(var (fieldName, _) in scenario.Fields)
        {
            if(!assignedFields.Contains(fieldName))
            {
                return false;
            }
        }

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 15: C# emission of default expressions
    //
    // **Validates: Requirements 12.3**
    //
    // For any default(TypeName) expression or bare default literal expression
    // targeting a defaultable type (class or struct), the C# emitter SHALL
    // emit a call to the corresponding type's __CreateDefault() factory
    // method.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 50,
        Arbitrary = new[] { typeof(DefaultEmitterPropertyTests) })]
    public bool DefaultExpression_EmitsCreateDefaultCall(DefaultExpressionScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildDefaultExpressionAst(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false;
        }

        // 1. Verify __CreateDefault() factory method declaration exists
        var methodMatches = CreateDefaultMethodPattern().Matches(csContent);
        var hasFactoryMethod = methodMatches
            .Cast<Match>()
            .Any(m => m.Groups[1].Value == scenario.TypeName);

        if(!hasFactoryMethod)
        {
            return false;
        }

        // 2. Verify the default expression was emitted as TypeName.__CreateDefault()
        var callMatches = CreateDefaultCallPattern().Matches(csContent);
        var callTypeNames = callMatches
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();

        // There should be at least one call to TypeName.__CreateDefault() in the
        // Main method body (the expression site), beyond the factory method's own
        // internal calls.
        // The factory method itself may call __CreateDefault for nested types,
        // but the Main method should contain the expression-site call.
        var hasExpressionSiteCall = callTypeNames.Contains(scenario.TypeName);

        return hasExpressionSiteCall;
    }
}