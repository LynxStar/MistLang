// Feature: abstract-member-dispatch, Property 3: Class C# emission includes complete inheritance clause
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
/// For any ClassType with a resolved BaseType and/or a non-empty Abstracts list,
/// the emitted C# class declaration SHALL contain an inheritance clause listing the
/// base class name (if present) followed by all abstract names in the same order as
/// ClassType.Abstracts. When both BaseType is null and Abstracts is empty, the
/// emitted declaration SHALL contain no inheritance clause.
///
/// Note: The current Storm AST generator only captures a single BaseClassName from
/// the inheritance clause. This means:
/// - A class with a base class has BaseType set, Abstracts empty
/// - A class with an abstract has BaseType null, Abstracts = [that abstract]
/// - A class with no inheritance has both null/empty
/// The property test covers all three cases through the AST pipeline.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public sealed partial class ClassAbstractEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public ClassAbstractEmissionPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistClassAbstractEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    private static AbstractMethodDeclarationNode MakeAbstractMethod(string name)
    {
        return new AbstractMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
        };
    }

    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<AbstractMethodDeclarationNode> methods,
        string? parentAbstractName = null)
    {
        var members = methods.Select(m =>
            (AbstractMemberDeclarationNode)new AbstractMethodMemberAbstractMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if(parentAbstractName is not null)
        {
            var _abstractList1 = MakeBaseList(parentAbstractName);

            decl.AbstractList = _abstractList1;

            decl.Abstracts = _abstractList1;
        }

        return decl;
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseOrAbstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseOrAbstractName is not null)
        {
            var _baseList1 = MakeBaseList(baseOrAbstractName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static TypeDeclarationNode WrapAbstract(AbstractDeclarationNode decl)
    {
        return new AbstractTypeTypeDeclarationNode { AbstractDeclaration = decl };
    }

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl)
    {
        return new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };
    }

    private static ClassDeclarationNode MakeProgramClass()
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
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

        var csFile = Directory.GetFiles(outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
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

    // Regex to extract class declarations with full inheritance clause.
    // Captures: class name (group 1) and optional inheritance clause (group 2).
    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*class\s+(\w+)(?:\s*:\s*(.+?))?\s*$", RegexOptions.Multiline)]
    private static partial Regex ClassDeclWithInheritancePattern();

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// The shape of the class declaration scenario.
    /// </summary>
#pragma warning disable CA1034
    public enum ClassScenarioKind
    {
        /// <summary>Class with no base class and no abstracts.</summary>
        NoInheritance,
        /// <summary>Class with a base class only (no abstracts).</summary>
        BaseClassOnly,
        /// <summary>Class with one abstract only (no base class).</summary>
        AbstractOnly,
    }

    /// <summary>
    /// A test scenario: a class with optional base class and optional abstract.
    /// The semantic model resolves the AST's single BaseClassName as either a
    /// base class (ClassType) or an abstract (AbstractType).
    /// </summary>
    public record ClassEmissionScenario(
        ClassScenarioKind Kind,
        string ClassName,
        string? BaseClassName,
        string? AbstractName)
    {
        /// <summary>
        /// The expected inheritance clause parts in order.
        /// </summary>
        public ImmutableArray<string> ExpectedInheritanceParts
        {
            get
            {
                var parts = ImmutableArray.CreateBuilder<string>();
                if(BaseClassName is not null)
                {
                    parts.Add(BaseClassName);
                }

                if(AbstractName is not null)
                {
                    parts.Add(AbstractName);
                }

                return parts.ToImmutable();
            }
        }

        public override string ToString()
        {
            return Kind switch
            {
                ClassScenarioKind.NoInheritance => $"class {ClassName}",
                ClassScenarioKind.BaseClassOnly => $"class {ClassName} : {BaseClassName}",
                ClassScenarioKind.AbstractOnly => $"class {ClassName} : {AbstractName}",
                _ => $"class {ClassName}",
            };
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate"];

    private static readonly string[] BaseClassNamePool =
        ["Animal", "Vehicle", "Shape", "Widget", "Entity"];

    private static readonly string[] ClassNamePool =
        ["Dog", "Car", "Circle", "Button", "Player"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where the class has no base class and no abstracts.
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
    /// and Abstracts is empty.
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
    /// Generates a scenario where the class has one abstract only (no base class).
    /// The AST's BaseClassName resolves to an AbstractType, so BaseType is null
    /// and Abstracts = [that abstract].
    /// Validates Requirements 2.2, 2.4.
    /// </summary>
    private static Gen<ClassEmissionScenario> GenAbstractOnly()
    {
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from classIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               select new ClassEmissionScenario(
                   ClassScenarioKind.AbstractOnly,
                   ClassNamePool[classIdx],
                   null,
                   AbstractNamePool[abstractIdx]);
    }

    public static Gen<ClassEmissionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNoInheritance(),
            GenBaseClassOnly(),
            GenAbstractOnly());
    }

    public static Arbitrary<ClassEmissionScenario> ArbitraryClassEmissionScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from the scenario.
    /// Declares any abstracts first, then the optional base class, then the
    /// test class, and finally a Program class with Main.
    /// </summary>
    private static CompilationUnitNode BuildAst(ClassEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Declare the abstract (if any)
        if(scenario.AbstractName is not null)
        {
            var method = MakeAbstractMethod(MethodNamePool[0]);
            var abstractDecl = MakeAbstractDecl(scenario.AbstractName,
                ImmutableArray.Create(method));
            typeDecls.Add(WrapAbstract(abstractDecl));
        }

        // 2. Declare the base class (if any)
        if(scenario.BaseClassName is not null)
        {
            typeDecls.Add(WrapClass(MakeClassDecl(scenario.BaseClassName)));
        }

        // 3. Declare the test class
        // The AST's BaseClassName is set to the first name in the inheritance clause.
        string? astBaseClassName = scenario.Kind switch
        {
            ClassScenarioKind.BaseClassOnly => scenario.BaseClassName,
            ClassScenarioKind.AbstractOnly => scenario.AbstractName,
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

        // Declare abstract
        if(scenario.AbstractName is not null)
        {
            _ = sb.AppendLine($"abstract {scenario.AbstractName} {{ void {MethodNamePool[0]}(); }}");
        }

        // Declare base class
        if(scenario.BaseClassName is not null)
        {
            _ = sb.AppendLine($"class {scenario.BaseClassName} {{ }}");
        }

        // Declare test class with inheritance clause
        var parts = scenario.ExpectedInheritanceParts;
        if(parts.Length > 0)
        {
            _ = sb.AppendLine($"class {scenario.ClassName} : {string.Join(", ", parts)} {{ }}");
        }
        else
        {
            _ = sb.AppendLine($"class {scenario.ClassName} {{ }}");
        }

        _ = sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 3: Class C# emission includes complete inheritance clause
    //
    // **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    //
    // For any ClassType with a resolved BaseType and/or a non-empty
    // Abstracts list, the emitted C# class declaration SHALL contain an
    // inheritance clause listing the base class name (if present) followed
    // by all abstract names in the same order as ClassType.Abstracts.
    // When both BaseType is null and Abstracts is empty, the emitted
    // declaration SHALL contain no inheritance clause.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ClassAbstractEmissionPropertyTests) })]
    public bool ClassCSharpEmission_IncludesCompleteInheritanceClause(ClassEmissionScenario scenario)
    {
        CleanOutputDir();

        var ast = BuildAst(scenario);
        var source = BuildSource(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false;
        }

        // Parse all class declarations from the emitted C#
        var matches = ClassDeclWithInheritancePattern().Matches(csContent);
        var emittedClasses = new Dictionary<string, string?>();
        foreach(Match match in matches)
        {
            var className = match.Groups[1].Value;
            var inheritanceClause = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;
            emittedClasses[className] = inheritanceClause;
        }

        // Verify the test class is present in the emitted output
        if(!emittedClasses.TryGetValue(scenario.ClassName, out var actualClause))
        {
            return false;
        }

        // Build the expected inheritance clause
        var expectedParts = scenario.ExpectedInheritanceParts;

        if(expectedParts.Length == 0)
        {
            // Requirement 2.3: No inheritance clause when no base class and no abstracts
            return actualClause is null;
        }

        // Requirements 2.1, 2.2, 2.4: Inheritance clause with correct order
        if(actualClause is null)
        {
            return false;
        }

        var expectedClause = string.Join(", ", expectedParts);

        // The emitted clause should match exactly (base class first, then abstracts in order)
        return actualClause == expectedClause;
        ;
    }

}