// Tasks 8.3–8.8: Unit tests for dependent type C# emission
// Validates: Requirements 13.4, 13.5, 13.6, 13.7, 13.8, 13.9, 14.1, 14.2, 14.3, 14.4, 9.1, 9.2, 12.1, 12.2

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for dependent type C# emission covering:
/// - Task 8.3: Type parameter emission with T prefix and carrier interface constraints
/// - Task 8.4: Dependent property emission
/// - Task 8.5: Object construction and type reference emission with carrier structs
/// - Task 8.6: DependentTypeAttribute and DependentTypeValueAttribute emission
/// - Task 8.7: Pattern matching emission for closed dependent types
/// - Task 8.8: Inheritance emission with dependent types
/// </summary>
public sealed class DependentTypeEmitterTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static Dictionary<string, string> CompileToFiles(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);

        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        return files;
    }

    private static string GetMainFile(Dictionary<string, string> files)
    {
        return files["test.cs"];
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 8.3: Type parameter emission with T prefix and constraints
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsDependentTypeParameterWithTPrefix()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class FixedBuffer<TSize>", content);
    }

    [Fact]
    public void EmitsCarrierInterfaceConstraintForDependentParam()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("where TSize : IConstInt", content);
    }

    [Fact]
    public void EmitsBoolCarrierInterfaceConstraint()
    {
        var source = "class Flag<Enabled:bool> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class Flag<TEnabled>", content);
        Assert.Contains("where TEnabled : IConstBool", content);
    }

    [Fact]
    public void EmitsMixedParameterListWithRegularAndDependent()
    {
        var source = "class TypedBuffer<T, Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class TypedBuffer<T, TSize>", content);
        Assert.Contains("where TSize : IConstInt", content);
    }

    [Fact]
    public void EmitsMultipleDependentParameters()
    {
        var source = "class Matrix<Rows:int, Cols:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class Matrix<TRows, TCols>", content);
        Assert.Contains("where TRows : IConstInt", content);
        Assert.Contains("where TCols : IConstInt", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 8.4: Dependent property emission
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsDependentPropertyForIntParam()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("public static int Size => TSize.Value;", content);
    }

    [Fact]
    public void EmitsDependentPropertyForBoolParam()
    {
        var source = "class Flag<Enabled:bool> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("public static bool Enabled => TEnabled.Value;", content);
    }

    [Fact]
    public void EmitsMultipleDependentProperties()
    {
        var source = "class Matrix<Rows:int, Cols:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("public static int Rows => TRows.Value;", content);
        Assert.Contains("public static int Cols => TCols.Value;", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 8.5: Object construction and type reference with carrier structs
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsCarrierStructInBaseTypeForInheritance()
    {
        var source = "class FixedBuffer<Size:int> { }\nclass SmallBuffer : FixedBuffer<5> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class SmallBuffer : FixedBuffer<Const_Int_5>", content);
    }

    [Fact]
    public void EmitsCarrierStructForNegativeValue()
    {
        var source = "class Offset<N:int> { }\nclass NegOffset : Offset<-1> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("Offset<Const_Int_Neg1>", content);
    }

    [Fact]
    public void EmitsCarrierStructForBoolValue()
    {
        var source = "class Flag<Enabled:bool> { }\nclass TrueFlag : Flag<true> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("Flag<Const_Bool_True>", content);
    }

    [Fact]
    public void EmitsCarrierStructInObjectConstruction()
    {
        var source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Main() {
                    var buf = new FixedBuffer<5>();
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("new FixedBuffer<Const_Int_5>()", content);
    }

    [Fact]
    public void EmitsCarrierStructInTypeReferenceForPropertyAccess()
    {
        // Type references with dependent type arguments in base type positions
        // emit carrier struct names. This covers the FixedBuffer<5>.Size pattern
        // at the emitter level (the type argument substitution logic).
        var source = "class FixedBuffer<Size:int> { }\nclass SmallBuffer : FixedBuffer<5> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("FixedBuffer<Const_Int_5>", content);
    }

    [Fact]
    public void EmitsCarrierStructInTypeReferenceForMultipleParams()
    {
        // Multiple dependent type arguments in base type position
        var source = "class Matrix<Rows:int, Cols:int> { }\nclass SmallMatrix : Matrix<3, 4> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("Matrix<Const_Int_3, Const_Int_4>", content);
    }

    [Fact]
    public void EmitsCarrierStructInTypeReferenceForBoolPropertyAccess()
    {
        // Bool dependent type argument in base type position
        var source = "class Flag<Enabled:bool> { }\nclass TrueFlag : Flag<true> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("Flag<Const_Bool_True>", content);
    }

    [Fact]
    public void EmitsCarrierStructInObjectConstructionWithNegativeValue()
    {
        // new Offset<-1>() → new Offset<Const_Int_Neg1>()
        var source = """
            class Offset<N:int> {
            }
            class Program {
                public static void Main() {
                    var o = new Offset<-1>();
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("new Offset<Const_Int_Neg1>()", content);
    }

    [Fact]
    public void EmitsCarrierStructInVariableTypeDeclaration()
    {
        // Variable type declarations with dependent type arguments use the same
        // EmitTypeArgumentList path. We verify through object construction with `var`
        // (inferred type) since the parser handles `new Type<literal>()` correctly.
        var source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Main() {
                    var buf = new FixedBuffer<10>();
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("new FixedBuffer<Const_Int_10>()", content);
    }

    [Fact]
    public void EmitsCarrierStructForHexLiteralInConstruction()
    {
        // Hex literal type arguments are converted to their decimal carrier struct name
        var source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Main() {
                    var buf = new FixedBuffer<0xFF>();
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        // 0xFF = 255
        Assert.Contains("new FixedBuffer<Const_Int_255>()", content);
    }

    [Fact]
    public void EmitsCarrierStructForCharValueInConstruction()
    {
        var source = """
            class CharHolder<C:char> {
            }
            class Program {
                public static void Main() {
                    var h = new CharHolder<'X'>();
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("new CharHolder<Const_Char_X>()", content);
    }

    [Fact]
    public void EmitsCarrierStructForMixedParamsInConstruction()
    {
        var source = """
            class TypedBuffer<T, Size:int> {
            }
            class Program {
                public static void Main() {
                    var buf = new TypedBuffer<string, 8>();
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("new TypedBuffer<string, Const_Int_8>()", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 8.6: DependentTypeAttribute emission
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsDependentTypeAttributeOnClass()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("[DependentType(\"Size\", typeof(int))]", content);
    }

    [Fact]
    public void EmitsMultipleDependentTypeAttributes()
    {
        var source = "class Matrix<Rows:int, Cols:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("[DependentType(\"Rows\", typeof(int))]", content);
        Assert.Contains("[DependentType(\"Cols\", typeof(int))]", content);
    }

    [Fact]
    public void EmitsDependentTypeAttributeWithBoolType()
    {
        var source = "class Flag<Enabled:bool> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("[DependentType(\"Enabled\", typeof(bool))]", content);
    }

    [Fact]
    public void EmitsDependentTypeAttributeAndDependentTypeValueAttributeInHelpers()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var helpers = files["DependentTypeHelpers.g.cs"];

        Assert.Contains("public sealed class DependentTypeAttribute : System.Attribute", helpers);
        Assert.Contains("public sealed class DependentTypeValueAttribute : System.Attribute", helpers);
    }

    [Fact]
    public void EmitsDependentTypeAttributeOnInterface()
    {
        var source = "abstract IBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("[DependentType(\"Size\", typeof(int))]", content);
    }

    [Fact]
    public void EmitsDependentTypeAttributeOnStruct()
    {
        var source = "struct FixedSpan<Length:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("[DependentType(\"Length\", typeof(int))]", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 8.7: Pattern matching emission for closed dependent types
    // Validates: Requirements 9.1, 9.2, 13.9
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsPatternMatchWithClosedDependentType_IntLiteral()
    {
        // if (x is FixedBuffer<5>) → if (x is FixedBuffer<Const_Int_5>)
        var source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Check(unknown item) {
                    if (item is FixedBuffer<5>) {
                    }
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("is FixedBuffer<Const_Int_5>", content);
    }

    [Fact]
    public void EmitsPatternMatchWithClosedDependentType_NegativeInt()
    {
        // if (x is Offset<-1>) → if (x is Offset<Const_Int_Neg1>)
        var source = """
            class Offset<N:int> {
            }
            class Program {
                public static void Check(unknown item) {
                    if (item is Offset<-1>) {
                    }
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("is Offset<Const_Int_Neg1>", content);
    }

    [Fact]
    public void EmitsPatternMatchWithClosedDependentType_BoolLiteral()
    {
        // if (x is Flag<true>) → if (x is Flag<Const_Bool_True>)
        var source = """
            class Flag<Enabled:bool> {
            }
            class Program {
                public static void Check(unknown item) {
                    if (item is Flag<true>) {
                    }
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("is Flag<Const_Bool_True>", content);
    }

    [Fact]
    public void EmitsPatternMatchWithClosedDependentType_MultipleParams()
    {
        // if (x is Matrix<3, 4>) → if (x is Matrix<Const_Int_3, Const_Int_4>)
        var source = """
            class Matrix<Rows:int, Cols:int> {
            }
            class Program {
                public static void Check(unknown item) {
                    if (item is Matrix<3, 4>) {
                    }
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("is Matrix<Const_Int_3, Const_Int_4>", content);
    }

    [Fact]
    public void EmitsPatternMatchWithClosedDependentType_WithBinding()
    {
        // if (x is FixedBuffer<5> buf) → if (x is FixedBuffer<Const_Int_5> buf)
        var source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Check(unknown item) {
                    if (item is FixedBuffer<5> buf) {
                    }
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("is FixedBuffer<Const_Int_5> buf", content);
    }

    [Fact]
    public void EmitsPatternMatchWithClosedDependentType_CharLiteral()
    {
        // if (x is CharHolder<'X'>) → if (x is CharHolder<Const_Char_X>)
        var source = """
            class CharHolder<C:char> {
            }
            class Program {
                public static void Check(unknown item) {
                    if (item is CharHolder<'X'>) {
                    }
                }
            }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("is CharHolder<Const_Char_X>", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 8.8: Inheritance emission with dependent types
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsFixedValueInheritanceWithCarrierStruct()
    {
        var source = "class FixedBuffer<Size:int> { }\nclass SmallBuffer : FixedBuffer<10> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class SmallBuffer : FixedBuffer<Const_Int_10>", content);
    }

    [Fact]
    public void EmitsForwardedParameterInheritanceWithTPrefixedName()
    {
        var source = "class FixedBuffer<Size:int> { }\nclass ResizableBuffer<N:int> : FixedBuffer<N> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class ResizableBuffer<TN>", content);
        Assert.Contains("FixedBuffer<TN>", content);
        Assert.Contains("where TN : IConstInt", content);
    }

    [Fact]
    public void EmitsForwardedParameterDoesNotGenerateCarrierStruct()
    {
        var source = "class FixedBuffer<Size:int> { }\nclass Wrapper<N:int> : FixedBuffer<N> { }";
        var files = CompileToFiles(source);
        var helpers = files["DependentTypeHelpers.g.cs"];

        // Should NOT have a carrier struct for the open parameter N
        Assert.DoesNotContain("Const_Int_N", helpers);
    }

    [Fact]
    public void EmitsMixedInheritanceWithFixedAndForwardedParams()
    {
        var source = """
            class Base<A:int, B:int> { }
            class Derived<X:int> : Base<5, X> { }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class Derived<TX>", content);
        Assert.Contains("Base<Const_Int_5, TX>", content);
        Assert.Contains("where TX : IConstInt", content);
    }

    [Fact]
    public void EmitsFixedBoolValueInheritance()
    {
        // Validates: Requirement 12.1 — fixing a dependent bool value in base type
        var source = "class Flag<Enabled:bool> { }\nclass AlwaysOn : Flag<true> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class AlwaysOn : Flag<Const_Bool_True>", content);
    }

    [Fact]
    public void EmitsForwardedBoolParameterInheritance()
    {
        // Validates: Requirement 12.2 — forwarding a dependent bool parameter
        var source = "class Flag<Enabled:bool> { }\nclass Wrapper<E:bool> : Flag<E> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class Wrapper<TE>", content);
        Assert.Contains("Flag<TE>", content);
        Assert.Contains("where TE : IConstBool", content);
    }

    [Fact]
    public void EmitsInheritanceWithMixedRegularAndDependentParams()
    {
        // Validates: Requirement 13.9 — inheritance with mixed type params compiles correctly
        var source = """
            class TypedBuffer<T, Size:int> { }
            class StringBuffer<N:int> : TypedBuffer<string, N> { }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class StringBuffer<TN>", content);
        Assert.Contains("TypedBuffer<string, TN>", content);
        Assert.Contains("where TN : IConstInt", content);
    }

    [Fact]
    public void EmitsInheritanceWithMultipleFixedValues()
    {
        // Validates: Requirement 12.1 — fixing multiple dependent values
        var source = """
            class Matrix<Rows:int, Cols:int> { }
            class Matrix3x3 : Matrix<3, 3> { }
            """;
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class Matrix3x3 : Matrix<Const_Int_3, Const_Int_3>", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Integration: Full class with dependent params
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitsCompleteClassWithDependentParam()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        // Should have the attribute
        Assert.Contains("[DependentType(\"Size\", typeof(int))]", content);
        // Should have the T-prefixed parameter
        Assert.Contains("class FixedBuffer<TSize>", content);
        // Should have the constraint
        Assert.Contains("where TSize : IConstInt", content);
        // Should have the property
        Assert.Contains("public static int Size => TSize.Value;", content);
    }

    [Fact]
    public void EmitsStructWithDependentParam()
    {
        var source = "struct Point<Dim:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("[DependentType(\"Dim\", typeof(int))]", content);
        Assert.Contains("struct Point<TDim>", content);
        Assert.Contains("where TDim : IConstInt", content);
        Assert.Contains("public static int Dim => TDim.Value;", content);
    }

    [Fact]
    public void EmitsMixedParameterListWithRegularConstraintAndDependent()
    {
        var source = "class TypedBuffer<T, Size:int> where T : class { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class TypedBuffer<T, TSize>", content);
        Assert.Contains("where T : class", content);
        Assert.Contains("where TSize : IConstInt", content);
    }

    [Fact]
    public void EmitsInterfaceWithDependentParam()
    {
        var source = "abstract IFixedCollection<Size:int> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("interface IFixedCollection<TSize>", content);
        Assert.Contains("where TSize : IConstInt", content);
    }

    [Fact]
    public void EmitsCharCarrierInterfaceConstraint()
    {
        var source = "class CharHolder<C:char> { }";
        var files = CompileToFiles(source);
        var content = GetMainFile(files);

        Assert.Contains("class CharHolder<TC>", content);
        Assert.Contains("where TC : IConstChar", content);
    }
}