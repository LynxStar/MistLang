// Feature: method-overloading, Property 14: Overload emission completeness
// Feature: method-overloading, Property 15: Round-trip compilation of overloaded programs
// **Validates: Requirements 7.1, 7.2, 7.3**

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Tests for C# emission of overloaded methods and constructors, and
/// round-trip compilation of overloaded Mist programs.
///
/// Property 14: Overload emission completeness
/// For a type with N overloaded methods (or constructors) sharing a name,
/// the C# emitter produces exactly N separate C# declarations.
///
/// Property 15: Round-trip compilation of overloaded programs
/// For a valid Mist program containing overloaded methods and/or constructors,
/// the full pipeline (parse → semantic model → type check → emit → Roslyn compile)
/// produces a successful C# compilation.
/// </summary>
public sealed partial class OverloadEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    // Shared driver for round-trip tests — Roslyn is expensive to spin up per call.
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    public OverloadEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistOverloadEmissionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
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

    // The 5 distinct primitive types used for overload signatures (0–4 index).
    private static readonly string[] PrimitiveTypeNames =
        ["int", "bool", "double", "float", "char"];

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } };
    }

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
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
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);
        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(_outputDir))
        {
            foreach(var file in Directory.GetFiles(_outputDir))
            {
                File.Delete(file);
            }
        }
    }

    [GeneratedRegex(@"(?:(?:public|private|static)\s+)*(?:void|int|bool|double|float|char|long|string|object)\s+(\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex MethodDeclPattern();

    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*(\w+)\s*\([^)]*\)(?:\s*:\s*(?:this|base)\s*\([^)]*\))?\s*$", RegexOptions.Multiline)]
    private static partial Regex ConstructorDeclPattern();

    // ─── AST builders ────────────────────────────────────────────────────

    private static (CompilationUnitNode Ast, string Source) BuildMethodOverloadAst(
        string methodName, IReadOnlyList<string> paramTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("class MyClass {");
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine();
        members.Add(new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = MakeToken("Main"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(), new StaticModifierModifierNode()),
            }
        });

        for(var i = 0; i < paramTypes.Count; i++)
        {
            var typeName = paramTypes[i];
            var paramName = $"p{i}";
            sb.AppendLine($"    public void {methodName}({typeName} {paramName}) {{ }}");

            var param = new ParameterNode
            {
                Type = MakePrimitiveTypeRef(typeName),
                Name = MakeToken(paramName),
            };
            var paramList = new ParameterListNode
            {
                Parameter = param,
                Span = new Storm.Runtime.SourceSpan(0, 0),
            };
            members.Add(new MethodMemberMemberDeclarationNode
            {
                Method = new MethodDeclarationNode
                {
                    Name = MakeToken(methodName),
                    ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                    Parameters = paramList,
                    Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                }
            });
        }

        sb.AppendLine("}");
        var source = sb.ToString();
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = members.ToImmutable(),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
        return (ast, source);
    }

    private static (CompilationUnitNode Ast, string Source) BuildConstructorOverloadAst(
        string className, IReadOnlyList<string> paramTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"class {className} {{");
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine();
        members.Add(new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = MakeToken("Main"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(), new StaticModifierModifierNode()),
            }
        });

        for(var i = 0; i < paramTypes.Count; i++)
        {
            var typeName = paramTypes[i];
            var paramName = $"p{i}";
            sb.AppendLine($"    public {className}({typeName} {paramName}) {{ }}");

            var param = new ParameterNode
            {
                Type = MakePrimitiveTypeRef(typeName),
                Name = MakeToken(paramName),
            };
            var paramList = new ParameterListNode
            {
                Parameter = param,
                Span = new Storm.Runtime.SourceSpan(0, 0),
            };
            members.Add(new ConstructorMemberMemberDeclarationNode
            {
                Constructor = new ConstructorDeclarationNode
                {
                    Name = MakeToken(className),
                    Parameters = paramList,
                    Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                }
            });
        }

        sb.AppendLine("}");
        var source = sb.ToString();
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = members.ToImmutable(),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
        return (ast, source);
    }

    private static string BuildMistSource(IReadOnlyList<string> paramTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("class Program {");
        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine();
        for(var i = 0; i < paramTypes.Count; i++)
        {
            sb.AppendLine($"    public void DoWork({paramTypes[i]} p{i}) {{ }}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static int CountConstructorDeclarations(string csContent, string className)
    {
        var pattern = $@"^\s*(?:(?:public|private)\s+)?{Regex.Escape(className)}\s*\(";
        return new Regex(pattern, RegexOptions.Multiline).Matches(csContent).Count;
    }

    // ─── Property 14: Overload emission completeness (methods) ───────────
    //
    // For each overload count 0–5, verify the emitter produces exactly that
    // many method declarations with the overloaded name.
    //
    // **Validates: Requirements 7.1, 7.2**

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OverloadedMethods_EmitExactlyNDeclarations(int overloadCount)
    {
        CleanOutputDir();
        var paramTypes = PrimitiveTypeNames.Take(overloadCount).ToList();
        var (ast, source) = BuildMethodOverloadAst("DoWork", paramTypes);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.False(string.IsNullOrEmpty(csContent), "Emitter produced no C# output");

        var matches = MethodDeclPattern().Matches(csContent);
        var actual = matches.Count(m => m.Groups[1].Value == "DoWork");
        Assert.Equal(overloadCount, actual);
    }

    // ─── Property 14: Overload emission completeness (constructors) ───────
    //
    // For each overload count 0–5, verify the emitter produces exactly that
    // many constructor declarations.
    //
    // **Validates: Requirements 7.1, 7.2**

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OverloadedConstructors_EmitExactlyNDeclarations(int overloadCount)
    {
        CleanOutputDir();
        var paramTypes = PrimitiveTypeNames.Take(overloadCount).ToList();
        var (ast, source) = BuildConstructorOverloadAst("MyClass", paramTypes);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.False(string.IsNullOrEmpty(csContent), "Emitter produced no C# output");

        var actual = CountConstructorDeclarations(csContent, "MyClass");
        Assert.Equal(overloadCount, actual);
    }

    // ─── Property 15: Round-trip compilation (methods) ───────────────────
    //
    // For each overload count 0–5, verify the full pipeline succeeds:
    // parse → semantic model → type check → emit → Roslyn compile.
    //
    // **Validates: Requirement 7.3**

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OverloadedProgram_RoundTripCompilationSucceeds(int overloadCount)
    {
        var paramTypes = PrimitiveTypeNames.Take(overloadCount).ToList();
        var mistSource = BuildMistSource(paramTypes);

        var tempDir = Path.Combine(Path.GetTempPath(), "MistRoundTrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: Path.Combine(tempDir, "out"),
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            Assert.True(result.Succeeded,
                $"Round-trip failed for {overloadCount} overload(s).\n" +
                string.Join("\n", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"  [{d.Id.Value}] {d.Message}")));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}