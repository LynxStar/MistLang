// Feature: interface-member-dispatch, Property 3: Class C# emission includes complete inheritance clause
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

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

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that the C# emitter includes the complete
/// inheritance clause on class declarations.
///
/// Property 3: Class C# emission includes complete inheritance clause
/// For any ClassType with a resolved BaseType and/or a non-empty Interfaces list,
/// the emitted C# class declaration SHALL contain an inheritance clause listing the
/// base class name (if present) followed by all interface names in the same order as
/// ClassType.Interfaces. When both BaseType is null and Interfaces is empty, the
/// emitted declaration SHALL contain no inheritance clause.
///
/// Note: The current Storm AST generator only captures a single BaseClassName from
/// the inheritance clause. This means:
/// - A class with a base class has BaseType set, Interfaces empty
/// - A class with an interface has BaseType null, Interfaces = [that interface]
/// - A class with no inheritance has both null/empty
/// The property test covers all three cases through the AST pipeline.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public sealed partial class ClassInterfaceEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public ClassInterfaceEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistClassIfaceEmissionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
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

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(string name)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
        };
    }

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods,
        string? parentInterfaceName = null)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if (parentInterfaceName is not null)
        {
            var _ifaceList1 = MakeBaseList(parentInterfaceName);

            decl.InterfaceList = _ifaceList1;

            decl.Interfaces = _ifaceList1;
        }

        return decl;
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseOrInterfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (baseOrInterfaceName is not null)
        {
            var _baseList1 = MakeBaseList(baseOrInterfaceName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static ClassDeclarationNode MakeProgramClass()
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        return new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
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
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    // Regex to extract class declarations with full inheritance clause.
    // Captures: class name (group 1) and optional inheritance clause (group 2).
    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*class\s+(\w+)(?:\s*:\s*(.+?))?$", RegexOptions.Multiline)]
    private static partial Regex ClassDeclWithInheritancePattern();

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// The shape of the class declaration scenario.
    /// </summary>
#pragma warning disable CA1034
    public enum ClassScenarioKind
    {
        /// <summary>Class with no base class and no interfaces.</summary>
        NoInheritance,
        /// <summary>Class with a base class only (no interfaces).</summary>
        BaseClassOnly,
        /// <summary>Class with one interface only (no base class).</summary>
        InterfaceOnly,
    }

    /// <summary>
    /// A test scenario: a class with optional base class and optional interface.
    /// The semantic model resolves the AST's single BaseClassName as either a
    /// base class (ClassType) or an interface (InterfaceType).
    /// </summary>
    public record ClassEmissionScenario(
        ClassScenarioKind Kind,
        string ClassName,
        string? BaseClassName,
        string? InterfaceName)
    {
        /// <summary>
        /// The expected inheritance clause parts in order.
        /// </summary>
        public ImmutableArray<string> ExpectedInheritanceParts
        {
            get
            {
                var parts = ImmutableArray.CreateBuilder<string>();
                if (BaseClassName is not null)
                    parts.Add(BaseClassName);
                if (InterfaceName is not null)
                    parts.Add(InterfaceName);
                return parts.ToImmutable();
            }
        }

        public override string ToString()
        {
            return Kind switch
            {
                ClassScenarioKind.NoInheritance => $"class {ClassName}",
                ClassScenarioKind.BaseClassOnly => $"class {ClassName} : {BaseClassName}",
                ClassScenarioKind.InterfaceOnly => $"class {ClassName} : {InterfaceName}",
                _ => $"class {ClassName}",
            };
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate"];

    private static readonly string[] BaseClassNamePool =
        ["Animal", "Vehicle", "Shape", "Widget", "Entity"];

    private static readonly string[] ClassNamePool =
        ["Dog", "Car", "Circle", "Button", "Player"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where the class has no base class and no interfaces.
    /// Validates Requirement 2.3.
    /// </summary>
    private static Gen<ClassEmissionScenario> GenNoInheritance()
    {
        return from classIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               select new ClassEmissionScenario(
                   ClassScenarioKind.NoInheritance,
                   ClassNamePool[classIdx],
                   null,
                   null);
    }

    /// <summary>
    /// Generates a scenario where the class has a base class only.
    /// The AST's BaseClassName resolves to a ClassType, so BaseType is set
    /// and Interfaces is empty.
    /// Validates Requirement 2.1 (base class part of inheritance clause).
    /// </summary>
    private static Gen<ClassEmissionScenario> GenBaseClassOnly()
    {
        return from baseIdx in Gen.Choose(0, BaseClassNamePool.Length - 1)
               from classIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               select new ClassEmissionScenario(
                   ClassScenarioKind.BaseClassOnly,
                   ClassNamePool[classIdx],
                   BaseClassNamePool[baseIdx],
                   null);
    }

    /// <summary>
    /// Generates a scenario where the class has one interface only (no base class).
    /// The AST's BaseClassName resolves to an InterfaceType, so BaseType is null
    /// and Interfaces = [that interface].
    /// Validates Requirements 2.2, 2.4.
    /// </summary>
    private static Gen<ClassEmissionScenario> GenInterfaceOnly()
    {
        return from ifaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from classIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               select new ClassEmissionScenario(
                   ClassScenarioKind.InterfaceOnly,
                   ClassNamePool[classIdx],
                   null,
                   InterfaceNamePool[ifaceIdx]);
    }

    public static Gen<ClassEmissionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNoInheritance(),
            GenBaseClassOnly(),
            GenInterfaceOnly());
    }

    public static Arbitrary<ClassEmissionScenario> ArbitraryClassEmissionScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from the scenario.
    /// Declares any interfaces first, then the optional base class, then the
    /// test class, and finally a Program class with Main.
    /// </summary>
    private static CompilationUnitNode BuildAst(ClassEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Declare the interface (if any)
        if (scenario.InterfaceName is not null)
        {
            var method = MakeInterfaceMethod(MethodNamePool[0]);
            var interfaceDecl = MakeInterfaceDecl(scenario.InterfaceName,
                ImmutableArray.Create(method));
            typeDecls.Add(WrapInterface(interfaceDecl));
        }

        // 2. Declare the base class (if any)
        if (scenario.BaseClassName is not null)
        {
            typeDecls.Add(WrapClass(MakeClassDecl(scenario.BaseClassName)));
        }

        // 3. Declare the test class
        // The AST's BaseClassName is set to the first name in the inheritance clause.
        string? astBaseClassName = scenario.Kind switch
        {
            ClassScenarioKind.BaseClassOnly => scenario.BaseClassName,
            ClassScenarioKind.InterfaceOnly => scenario.InterfaceName,
            _ => null,
        };

        typeDecls.Add(WrapClass(MakeClassDecl(scenario.ClassName, astBaseClassName)));

        // 4. Program class with Main entry point
        typeDecls.Add(WrapClass(MakeProgramClass()));

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a minimal source string matching the AST structure.
    /// </summary>
    private static string BuildSource(ClassEmissionScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        // Declare interface
        if (scenario.InterfaceName is not null)
        {
            sb.AppendLine($"interface {scenario.InterfaceName} {{ void {MethodNamePool[0]}(); }}");
        }

        // Declare base class
        if (scenario.BaseClassName is not null)
        {
            sb.AppendLine($"class {scenario.BaseClassName} {{ }}");
        }

        // Declare test class with inheritance clause
        var parts = scenario.ExpectedInheritanceParts;
        if (parts.Length > 0)
            sb.AppendLine($"class {scenario.ClassName} : {string.Join(", ", parts)} {{ }}");
        else
            sb.AppendLine($"class {scenario.ClassName} {{ }}");

        sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 3: Class C# emission includes complete inheritance clause
    //
    // **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    //
    // For any ClassType with a resolved BaseType and/or a non-empty
    // Interfaces list, the emitted C# class declaration SHALL contain an
    // inheritance clause listing the base class name (if present) followed
    // by all interface names in the same order as ClassType.Interfaces.
    // When both BaseType is null and Interfaces is empty, the emitted
    // declaration SHALL contain no inheritance clause.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ClassInterfaceEmissionPropertyTests) })]
    public bool ClassCSharpEmission_IncludesCompleteInheritanceClause(ClassEmissionScenario scenario)
    {
        CleanOutputDir();

        var ast = BuildAst(scenario);
        var source = BuildSource(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Parse all class declarations from the emitted C#
        var matches = ClassDeclWithInheritancePattern().Matches(csContent);
        var emittedClasses = new Dictionary<string, string?>();
        foreach (Match match in matches)
        {
            var className = match.Groups[1].Value;
            var inheritanceClause = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;
            emittedClasses[className] = inheritanceClause;
        }

        // Verify the test class is present in the emitted output
        if (!emittedClasses.TryGetValue(scenario.ClassName, out var actualClause))
            return false;

        // Build the expected inheritance clause
        var expectedParts = scenario.ExpectedInheritanceParts;

        if (expectedParts.Length == 0)
        {
            // Requirement 2.3: No inheritance clause when no base class and no interfaces
            return actualClause is null;
        }

        // Requirements 2.1, 2.2, 2.4: Inheritance clause with correct order
        if (actualClause is null)
            return false;

        var expectedClause = string.Join(", ", expectedParts);

        // The emitted clause should match exactly (base class first, then interfaces in order)
        return actualClause == expectedClause;
    ;
    }

}
