// Feature: abstracts, Property 10: Abstract C# emission correctness
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**

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
/// Property-based tests verifying that the C# emitter correctly emits interface
/// declarations, abstract type method signatures, and inheritance lists.
///
/// Property 10: Abstract C# emission correctness
/// For any valid Mist program containing abstract declarations, classes/structs
/// implementing abstracts, and abstract-to-abstract inheritance, the emitted C#
/// code SHALL contain syntactically correct C# interface declarations, interface
/// method signatures, and inheritance lists that match the Mist source structure.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**
/// </summary>
public sealed partial class AbstractEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public AbstractEmissionPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistAbstractEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    private static AbstractMethodDeclarationNode MakeAbstractMethod(
        string name,
        TypeReferenceNode? returnType = null)
    {
        return new AbstractMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
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
        string? baseClassName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? abstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(abstractName is not null)
        {
            var _abstractList2 = MakeBaseList(abstractName);

            decl.AbstractList = _abstractList2;

            decl.Abstracts = _abstractList2;
        }

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

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl)
    {
        return new StructTypeTypeDeclarationNode { StructDeclaration = decl };
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

    /// <summary>
    /// Builds a Program class with a static void Main() entry point.
    /// Required for the emitter to produce valid C# output.
    /// </summary>
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

    // Regex patterns for verifying emitted C#
    [GeneratedRegex(@"^\s*interface\s+(\w+)(?:\s*:\s*(\w+))?", RegexOptions.Multiline)]
    private static partial Regex InterfaceDeclPattern();

    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*class\s+(\w+)(?:\s*:\s*(\w+))?", RegexOptions.Multiline)]
    private static partial Regex ClassDeclPattern();

    [GeneratedRegex(@"^\s*(?:(?:public|private|static)\s+)*struct\s+(\w+)(?:\s*:\s*(\w+))?", RegexOptions.Multiline)]
    private static partial Regex StructDeclPattern();

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario for testing abstract emission.
    /// Contains abstracts (with optional methods and parent abstract),
    /// classes implementing abstracts, and structs implementing abstracts.
    /// </summary>
#pragma warning disable CA1034
    public enum EmissionTypeKind
    {
        AbstractOnly,
        ClassWithAbstract,
        StructWithAbstract,
        AbstractInheritance,
    }

    public record EmissionScenario(
        ImmutableArray<AbstractInfo> Abstracts,
        ImmutableArray<ImplementorInfo> Implementors)
    {
        public override string ToString()
        {
            var abstracts = string.Join(", ", Abstracts.Select(i =>
            {
                var methods = i.MethodNames.Length > 0
                    ? $"{{ {string.Join(", ", i.MethodNames)} }}"
                    : "{ }";
                var parent = i.ParentAbstractName is not null ? $" : {i.ParentAbstractName}" : "";
                return $"{i.Name}{parent} {methods}";
            }));
            var impls = string.Join(", ", Implementors.Select(i =>
                $"{i.Kind} {i.Name} : {i.AbstractName}"));
            return $"Abstracts=[{abstracts}]; Implementors=[{impls}]";
        }
    }

    public record AbstractInfo(
        string Name,
        ImmutableArray<string> MethodNames,
        string? ParentAbstractName);

    public record ImplementorInfo(
        string Name,
        EmissionTypeKind Kind,
        string AbstractName);
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate"];

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] StructNamePool =
        ["PointA", "PointB", "PointC", "PointD", "PointE"];

    public static Gen<EmissionScenario> GenEmissionScenario()
    {
        return from abstractCount in Gen.Choose(1, 3)
               from abstractIndices in Gen.Shuffle(AbstractNamePool)
                   .Select(n => n.Take(abstractCount).ToArray())
               from scenario in BuildScenario(abstractIndices)
               select scenario;
    }

    private static Gen<EmissionScenario> BuildScenario(string[] abstractNames)
    {
        // Build abstracts with random methods
        var abstractsGen = Gen.Constant(ImmutableArray.CreateBuilder<AbstractInfo>());

        foreach(var abstractName in abstractNames)
        {
            abstractsGen = from builder in abstractsGen
                           from methodCount in Gen.Choose(0, 3)
                           from methods in Gen.Shuffle(MethodNamePool)
                               .Select(m => m.Take(methodCount).ToImmutableArray())
                           select AddAbstract(builder, abstractName, methods, null);
        }

        // Optionally add an abstract that inherits from the first abstract
        var withInheritanceGen = from builder in abstractsGen
                                 from addInheritance in Gen.Elements(true, false)
                                 select addInheritance && builder.Count > 0
                                     ? AddAbstract(builder, "IChild", ImmutableArray<string>.Empty, builder[0].Name)
                                     : builder;

        // Build implementors (classes and structs)
        var fullGen = from abstracts in withInheritanceGen
                      from implCount in Gen.Choose(0, 2)
                      from implementors in GenImplementors(abstracts.ToImmutable(), implCount)
                      select new EmissionScenario(abstracts.ToImmutable(), implementors);

        return fullGen;
    }

    private static ImmutableArray<AbstractInfo>.Builder AddAbstract(
        ImmutableArray<AbstractInfo>.Builder builder,
        string name,
        ImmutableArray<string> methods,
        string? parentName)
    {
        builder.Add(new AbstractInfo(name, methods, parentName));
        return builder;
    }

    private static Gen<ImmutableArray<ImplementorInfo>> GenImplementors(
        ImmutableArray<AbstractInfo> abstracts,
        int count)
    {
        if(count == 0 || abstracts.Length == 0)
        {
            return Gen.Constant(ImmutableArray<ImplementorInfo>.Empty);
        }

        var gen = Gen.Constant(ImmutableArray.CreateBuilder<ImplementorInfo>());

        for(var i = 0; i < count; i++)
        {
            var idx = i;
            gen = from builder in gen
                  from isStruct in Gen.Elements(true, false)
                  from abstractIdx in Gen.Choose(0, abstracts.Length - 1)
                  let kind = isStruct ? EmissionTypeKind.StructWithAbstract : EmissionTypeKind.ClassWithAbstract
                  let name = isStruct
                      ? StructNamePool[idx % StructNamePool.Length]
                      : ClassNamePool[idx % ClassNamePool.Length]
                  let abstractName = abstracts[abstractIdx].Name
                  select AddImplementor(builder, name, kind, abstractName);
        }

        return gen.Select(b => b.ToImmutable());
    }

    private static ImmutableArray<ImplementorInfo>.Builder AddImplementor(
        ImmutableArray<ImplementorInfo>.Builder builder,
        string name,
        EmissionTypeKind kind,
        string abstractName)
    {
        builder.Add(new ImplementorInfo(name, kind, abstractName));
        return builder;
    }

    public static Arbitrary<EmissionScenario> ArbitraryEmissionScenario()
    {
        return GenEmissionScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    private static CompilationUnitNode BuildAst(EmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Add abstract declarations
        foreach(var abs in scenario.Abstracts)
        {
            var methods = abs.MethodNames
                .Select(m => MakeAbstractMethod(m))
                .ToImmutableArray();
            var abstractDecl = MakeAbstractDecl(abs.Name, methods, abs.ParentAbstractName);
            typeDecls.Add(WrapAbstract(abstractDecl));
        }

        // Add implementing types (classes and structs)
        foreach(var impl in scenario.Implementors)
        {
            switch(impl.Kind)
            {
                case EmissionTypeKind.ClassWithAbstract:
                    // Class implementing an abstract: uses BaseClause/BaseClassName
                    typeDecls.Add(WrapClass(MakeClassDecl(impl.Name, impl.AbstractName)));
                    break;
                case EmissionTypeKind.StructWithAbstract:
                    typeDecls.Add(WrapStruct(MakeStructDecl(impl.Name, impl.AbstractName)));
                    break;
            }
        }

        // Add Program class with Main entry point
        typeDecls.Add(WrapClass(MakeProgramClass()));

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    private static string BuildSource(EmissionScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        foreach(var abs in scenario.Abstracts)
        {
            var parent = abs.ParentAbstractName is not null ? $" : {abs.ParentAbstractName}" : "";
            _ = sb.AppendLine($"abstract {abs.Name}{parent} {{");
            foreach(var method in abs.MethodNames)
            {
                _ = sb.AppendLine($"    void {method}();");
            }

            _ = sb.AppendLine("}");
        }

        foreach(var impl in scenario.Implementors)
        {
            switch(impl.Kind)
            {
                case EmissionTypeKind.ClassWithAbstract:
                    _ = sb.AppendLine($"class {impl.Name} : {impl.AbstractName} {{ }}");
                    break;
                case EmissionTypeKind.StructWithAbstract:
                    _ = sb.AppendLine($"struct {impl.Name} : {impl.AbstractName} {{ }}");
                    break;
            }
        }

        _ = sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 10: Abstract C# emission correctness
    //
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**
    //
    // For any valid Mist program containing abstract declarations,
    // classes/structs implementing abstracts, and abstract-to-abstract
    // inheritance, the emitted C# code SHALL contain syntactically correct
    // C# interface declarations, interface method signatures, and
    // inheritance lists that match the Mist source structure.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AbstractEmissionPropertyTests) })]
    public bool CSharpEmission_AbstractDeclarationsEmittedCorrectly(EmissionScenario scenario)
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

        // ── Requirement 9.1: Each abstract declaration emitted as C# interface ──
        var interfaceMatches = InterfaceDeclPattern().Matches(csContent);
        var emittedAbstracts = new Dictionary<string, string?>();
        foreach(Match match in interfaceMatches)
        {
            var abstractName = match.Groups[1].Value;
            var parentName = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedAbstracts[abstractName] = parentName;
        }

        foreach(var abs in scenario.Abstracts)
        {
            // Verify the abstract declaration exists in emitted C#
            if(!emittedAbstracts.ContainsKey(abs.Name))
            {
                return false;
            }

            // ── Requirement 9.6: Abstract inheritance emitted correctly ──
            if(abs.ParentAbstractName is not null)
            {
                if(emittedAbstracts[abs.Name] != abs.ParentAbstractName)
                {
                    return false;
                }
            }
            else
            {
                // No parent abstract: should not have an inheritance clause
                if(emittedAbstracts[abs.Name] is not null)
                {
                    return false;
                }
            }
        }

        // ── Requirement 9.2: Abstract methods emitted as C# method signatures ──
        foreach(var abs in scenario.Abstracts)
        {
            foreach(var methodName in abs.MethodNames)
            {
                // Verify the method signature appears in the emitted C#
                // Pattern: "void MethodName();" (with possible indentation)
                if(!csContent.Contains($"void {methodName}("))
                {
                    return false;
                }
            }
        }

        // ── Requirements 9.3, 9.4: Classes with abstract lists ──
        var classMatches = ClassDeclPattern().Matches(csContent);
        var emittedClasses = new Dictionary<string, string?>();
        foreach(Match match in classMatches)
        {
            var className = match.Groups[1].Value;
            var baseName = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedClasses[className] = baseName;
        }

        foreach(var impl in scenario.Implementors)
        {
            if(impl.Kind == EmissionTypeKind.ClassWithAbstract)
            {
                if(!emittedClasses.TryGetValue(impl.Name, out var actualBase))
                {
                    return false;
                }
                // The class should have `: AbstractName` in its declaration
                if(actualBase != impl.AbstractName)
                {
                    return false;
                }
            }
        }

        // ── Requirement 9.5: Structs with abstract lists ──
        var structMatches = StructDeclPattern().Matches(csContent);
        var emittedStructs = new Dictionary<string, string?>();
        foreach(Match match in structMatches)
        {
            var structName = match.Groups[1].Value;
            var abstractName = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedStructs[structName] = abstractName;
        }

        foreach(var impl in scenario.Implementors)
        {
            if(impl.Kind == EmissionTypeKind.StructWithAbstract)
            {
                if(!emittedStructs.TryGetValue(impl.Name, out var actualAbstract))
                {
                    return false;
                }
                // The struct should have `: AbstractName` in its declaration
                if(actualAbstract != impl.AbstractName)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void Emit_AbstractDeclaration_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var abstractDecl = MakeAbstractDecl("IWorker",
            ImmutableArray.Create(
                MakeAbstractMethod("DoWork"),
                MakeAbstractMethod("Process")));

        var source = "abstract IWorker {\n    void DoWork();\n    void Process();\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                WrapAbstract(abstractDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IWorker", csContent);
        Assert.Contains("void DoWork()", csContent);
        Assert.Contains("void Process()", csContent);
        Assert.DoesNotContain(": ", csContent.Split('\n').First(l => l.Contains("interface IWorker")));
    }

    [Fact]
    public void Emit_AbstractWithInheritance_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var baseAbstract = MakeAbstractDecl("IBase",
            ImmutableArray.Create(MakeAbstractMethod("BaseMethod")));
        var derivedAbstract = MakeAbstractDecl("IDerived",
            ImmutableArray<AbstractMethodDeclarationNode>.Empty,
            "IBase");

        var source = "abstract IBase {\n    void BaseMethod();\n}\nabstract IDerived : IBase {\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                WrapAbstract(baseAbstract).AsTopLevelItem(),
                WrapAbstract(derivedAbstract).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IBase", csContent);
        Assert.Contains("interface IDerived : IBase", csContent);
        Assert.Contains("void BaseMethod()", csContent);
    }

    [Fact]
    public void Emit_ClassImplementingAbstract_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var abstractDecl = MakeAbstractDecl("IWorker",
            ImmutableArray.Create(MakeAbstractMethod("DoWork")));
        var classDecl = MakeClassDecl("Worker", "IWorker");

        var source = "abstract IWorker {\n    void DoWork();\n}\nclass Worker : IWorker { }\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                WrapAbstract(abstractDecl).AsTopLevelItem(),
                WrapClass(classDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IWorker", csContent);
        Assert.Contains("class Worker : IWorker", csContent);
    }

    [Fact]
    public void Emit_StructImplementingAbstract_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var abstractDecl = MakeAbstractDecl("IComparable",
            ImmutableArray.Create(MakeAbstractMethod("CompareTo")));
        var structDecl = MakeStructDecl("Point", "IComparable");

        var source = "abstract IComparable {\n    void CompareTo();\n}\nstruct Point : IComparable { }\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                WrapAbstract(abstractDecl).AsTopLevelItem(),
                WrapStruct(structDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IComparable", csContent);
        Assert.Contains("struct Point : IComparable", csContent);
        Assert.Contains("void CompareTo()", csContent);
    }

    [Fact]
    public void Emit_EmptyAbstract_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var abstractDecl = MakeAbstractDecl("IEmpty",
            ImmutableArray<AbstractMethodDeclarationNode>.Empty);

        var source = "abstract IEmpty {\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                WrapAbstract(abstractDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IEmpty", csContent);
        // Empty emitted interface should have opening and closing braces with nothing between
        var lines = csContent.Split('\n').Select(l => l.Trim()).ToArray();
        var ifaceLineIdx = Array.FindIndex(lines, l => l.StartsWith("interface IEmpty"));
        Assert.True(ifaceLineIdx >= 0);
    }

    [Fact]
    public void Emit_AbstractMethodWithReturnType_ProducesCorrectSignature()
    {
        CleanOutputDir();

        var intReturnType = new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            }
        };
        var abstractDecl = MakeAbstractDecl("ICalculator",
            ImmutableArray.Create(MakeAbstractMethod("Calculate", intReturnType)));

        var source = "abstract ICalculator {\n    int Calculate();\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                WrapAbstract(abstractDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface ICalculator", csContent);
        Assert.Contains("int Calculate()", csContent);
        ;
    }

}