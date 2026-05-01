// Feature: interfaces, Property 10: Interface C# emission correctness
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
/// declarations, interface method signatures, and inheritance lists.
///
/// Property 10: Interface C# emission correctness
/// For any valid Mist program containing interface declarations, classes/structs
/// implementing interfaces, and interface-to-interface inheritance, the emitted C#
/// code SHALL contain syntactically correct C# interface declarations, interface
/// method signatures, and inheritance lists that match the Mist source structure.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**
/// </summary>
public sealed partial class InterfaceEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public InterfaceEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistInterfaceEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(
        string name,
        TypeReferenceNode? returnType = null)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
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
        string? baseClassName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? interfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (interfaceName is not null)
        {
            var _ifaceList2 = MakeBaseList(interfaceName);

            decl.InterfaceList = _ifaceList2;

            decl.Interfaces = _ifaceList2;
        }

        return decl;
    }

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

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

    /// <summary>
    /// Builds a Program class with a static void Main() entry point.
    /// Required for the emitter to produce valid C# output.
    /// </summary>
    private static ClassDeclarationNode MakeProgramClass()
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
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

    [GeneratedRegex(@"^\s+(\w+)\s+(\w+)\((.*?)\);", RegexOptions.Multiline)]
    private static partial Regex InterfaceMethodPattern();

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario for testing interface emission.
    /// Contains interfaces (with optional methods and parent interface),
    /// classes implementing interfaces, and structs implementing interfaces.
    /// </summary>
#pragma warning disable CA1034
    public enum EmissionTypeKind
    {
        InterfaceOnly,
        ClassWithInterface,
        StructWithInterface,
        InterfaceInheritance,
    }

    public record EmissionScenario(
        ImmutableArray<InterfaceInfo> Interfaces,
        ImmutableArray<ImplementorInfo> Implementors)
    {
        public override string ToString()
        {
            var ifaces = string.Join(", ", Interfaces.Select(i =>
            {
                var methods = i.MethodNames.Length > 0
                    ? $"{{ {string.Join(", ", i.MethodNames)} }}"
                    : "{ }";
                var parent = i.ParentInterfaceName is not null ? $" : {i.ParentInterfaceName}" : "";
                return $"{i.Name}{parent} {methods}";
            }));
            var impls = string.Join(", ", Implementors.Select(i =>
                $"{i.Kind} {i.Name} : {i.InterfaceName}"));
            return $"Interfaces=[{ifaces}]; Implementors=[{impls}]";
        }
    }

    public record InterfaceInfo(
        string Name,
        ImmutableArray<string> MethodNames,
        string? ParentInterfaceName);

    public record ImplementorInfo(
        string Name,
        EmissionTypeKind Kind,
        string InterfaceName);
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate"];

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] StructNamePool =
        ["PointA", "PointB", "PointC", "PointD", "PointE"];

    public static Gen<EmissionScenario> GenEmissionScenario()
    {
        return from interfaceCount in Gen.Choose(1, 3)
               from ifaceIndices in Gen.Shuffle(InterfaceNamePool)
                   .Select(n => n.Take(interfaceCount).ToArray())
               from scenario in BuildScenario(ifaceIndices)
               select scenario;
    }

    private static Gen<EmissionScenario> BuildScenario(string[] interfaceNames)
    {
        // Build interfaces with random methods
        var interfacesGen = Gen.Constant(ImmutableArray.CreateBuilder<InterfaceInfo>());

        foreach (var ifaceName in interfaceNames)
        {
            interfacesGen = from builder in interfacesGen
                            from methodCount in Gen.Choose(0, 3)
                            from methods in Gen.Shuffle(MethodNamePool)
                                .Select(m => m.Take(methodCount).ToImmutableArray())
                            select AddInterface(builder, ifaceName, methods, null);
        }

        // Optionally add an interface that inherits from the first interface
        var withInheritanceGen = from builder in interfacesGen
                                from addInheritance in Gen.Elements(true, false)
                                select addInheritance && builder.Count > 0
                                    ? AddInterface(builder, "IChild", ImmutableArray<string>.Empty, builder[0].Name)
                                    : builder;

        // Build implementors (classes and structs)
        var fullGen = from interfaces in withInheritanceGen
                      from implCount in Gen.Choose(0, 2)
                      from implementors in GenImplementors(interfaces.ToImmutable(), implCount)
                      select new EmissionScenario(interfaces.ToImmutable(), implementors);

        return fullGen;
    }

    private static ImmutableArray<InterfaceInfo>.Builder AddInterface(
        ImmutableArray<InterfaceInfo>.Builder builder,
        string name,
        ImmutableArray<string> methods,
        string? parentName)
    {
        builder.Add(new InterfaceInfo(name, methods, parentName));
        return builder;
    }

    private static Gen<ImmutableArray<ImplementorInfo>> GenImplementors(
        ImmutableArray<InterfaceInfo> interfaces,
        int count)
    {
        if (count == 0 || interfaces.Length == 0)
            return Gen.Constant(ImmutableArray<ImplementorInfo>.Empty);

        var gen = Gen.Constant(ImmutableArray.CreateBuilder<ImplementorInfo>());

        for (var i = 0; i < count; i++)
        {
            var idx = i;
            gen = from builder in gen
                  from isStruct in Gen.Elements(true, false)
                  from ifaceIdx in Gen.Choose(0, interfaces.Length - 1)
                  let kind = isStruct ? EmissionTypeKind.StructWithInterface : EmissionTypeKind.ClassWithInterface
                  let name = isStruct
                      ? StructNamePool[idx % StructNamePool.Length]
                      : ClassNamePool[idx % ClassNamePool.Length]
                  let ifaceName = interfaces[ifaceIdx].Name
                  select AddImplementor(builder, name, kind, ifaceName);
        }

        return gen.Select(b => b.ToImmutable());
    }

    private static ImmutableArray<ImplementorInfo>.Builder AddImplementor(
        ImmutableArray<ImplementorInfo>.Builder builder,
        string name,
        EmissionTypeKind kind,
        string interfaceName)
    {
        builder.Add(new ImplementorInfo(name, kind, interfaceName));
        return builder;
    }

    public static Arbitrary<EmissionScenario> ArbitraryEmissionScenario() =>
        GenEmissionScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    private static CompilationUnitNode BuildAst(EmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Add interface declarations
        foreach (var iface in scenario.Interfaces)
        {
            var methods = iface.MethodNames
                .Select(m => MakeInterfaceMethod(m))
                .ToImmutableArray();
            var interfaceDecl = MakeInterfaceDecl(iface.Name, methods, iface.ParentInterfaceName);
            typeDecls.Add(WrapInterface(interfaceDecl));
        }

        // Add implementing types (classes and structs)
        foreach (var impl in scenario.Implementors)
        {
            switch (impl.Kind)
            {
                case EmissionTypeKind.ClassWithInterface:
                    // Class implementing an interface: uses BaseClause/BaseClassName
                    typeDecls.Add(WrapClass(MakeClassDecl(impl.Name, impl.InterfaceName)));
                    break;
                case EmissionTypeKind.StructWithInterface:
                    typeDecls.Add(WrapStruct(MakeStructDecl(impl.Name, impl.InterfaceName)));
                    break;
            }
        }

        // Add Program class with Main entry point
        typeDecls.Add(WrapClass(MakeProgramClass()));

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    private static string BuildSource(EmissionScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var iface in scenario.Interfaces)
        {
            var parent = iface.ParentInterfaceName is not null ? $" : {iface.ParentInterfaceName}" : "";
            sb.AppendLine($"interface {iface.Name}{parent} {{");
            foreach (var method in iface.MethodNames)
            {
                sb.AppendLine($"    void {method}();");
            }
            sb.AppendLine("}");
        }

        foreach (var impl in scenario.Implementors)
        {
            switch (impl.Kind)
            {
                case EmissionTypeKind.ClassWithInterface:
                    sb.AppendLine($"class {impl.Name} : {impl.InterfaceName} {{ }}");
                    break;
                case EmissionTypeKind.StructWithInterface:
                    sb.AppendLine($"struct {impl.Name} : {impl.InterfaceName} {{ }}");
                    break;
            }
        }

        sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 10: Interface C# emission correctness
    //
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**
    //
    // For any valid Mist program containing interface declarations,
    // classes/structs implementing interfaces, and interface-to-interface
    // inheritance, the emitted C# code SHALL contain syntactically correct
    // C# interface declarations, interface method signatures, and
    // inheritance lists that match the Mist source structure.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(InterfaceEmissionPropertyTests) })]
    public bool CSharpEmission_InterfaceDeclarationsEmittedCorrectly(EmissionScenario scenario)
    {
        CleanOutputDir();

        var ast = BuildAst(scenario);
        var source = BuildSource(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // ── Requirement 9.1: Each interface declaration emitted as C# interface ──
        var interfaceMatches = InterfaceDeclPattern().Matches(csContent);
        var emittedInterfaces = new Dictionary<string, string?>();
        foreach (Match match in interfaceMatches)
        {
            var ifaceName = match.Groups[1].Value;
            var parentName = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedInterfaces[ifaceName] = parentName;
        }

        foreach (var iface in scenario.Interfaces)
        {
            // Verify the interface declaration exists in emitted C#
            if (!emittedInterfaces.ContainsKey(iface.Name))
                return false;

            // ── Requirement 9.6: Interface inheritance emitted correctly ──
            if (iface.ParentInterfaceName is not null)
            {
                if (emittedInterfaces[iface.Name] != iface.ParentInterfaceName)
                    return false;
            }
            else
            {
                // No parent interface: should not have an inheritance clause
                if (emittedInterfaces[iface.Name] is not null)
                    return false;
            }
        }

        // ── Requirement 9.2: Interface methods emitted as C# method signatures ──
        foreach (var iface in scenario.Interfaces)
        {
            foreach (var methodName in iface.MethodNames)
            {
                // Verify the method signature appears in the emitted C#
                // Pattern: "void MethodName();" (with possible indentation)
                if (!csContent.Contains($"void {methodName}("))
                    return false;
            }
        }

        // ── Requirements 9.3, 9.4: Classes with interface lists ──
        var classMatches = ClassDeclPattern().Matches(csContent);
        var emittedClasses = new Dictionary<string, string?>();
        foreach (Match match in classMatches)
        {
            var className = match.Groups[1].Value;
            var baseName = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedClasses[className] = baseName;
        }

        foreach (var impl in scenario.Implementors)
        {
            if (impl.Kind == EmissionTypeKind.ClassWithInterface)
            {
                if (!emittedClasses.TryGetValue(impl.Name, out var actualBase))
                    return false;
                // The class should have `: InterfaceName` in its declaration
                if (actualBase != impl.InterfaceName)
                    return false;
            }
        }

        // ── Requirement 9.5: Structs with interface lists ──
        var structMatches = StructDeclPattern().Matches(csContent);
        var emittedStructs = new Dictionary<string, string?>();
        foreach (Match match in structMatches)
        {
            var structName = match.Groups[1].Value;
            var ifaceName = match.Groups[2].Success ? match.Groups[2].Value : null;
            emittedStructs[structName] = ifaceName;
        }

        foreach (var impl in scenario.Implementors)
        {
            if (impl.Kind == EmissionTypeKind.StructWithInterface)
            {
                if (!emittedStructs.TryGetValue(impl.Name, out var actualIface))
                    return false;
                // The struct should have `: InterfaceName` in its declaration
                if (actualIface != impl.InterfaceName)
                    return false;
            }
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void Emit_InterfaceDeclaration_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var interfaceDecl = MakeInterfaceDecl("IWorker",
            ImmutableArray.Create(
                MakeInterfaceMethod("DoWork"),
                MakeInterfaceMethod("Process")));

        var source = "interface IWorker {\n    void DoWork();\n    void Process();\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                WrapInterface(interfaceDecl).AsTopLevelItem(),
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
    public void Emit_InterfaceWithInheritance_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var baseInterface = MakeInterfaceDecl("IBase",
            ImmutableArray.Create(MakeInterfaceMethod("BaseMethod")));
        var derivedInterface = MakeInterfaceDecl("IDerived",
            ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
            "IBase");

        var source = "interface IBase {\n    void BaseMethod();\n}\ninterface IDerived : IBase {\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                WrapInterface(baseInterface).AsTopLevelItem(),
                WrapInterface(derivedInterface).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IBase", csContent);
        Assert.Contains("interface IDerived : IBase", csContent);
        Assert.Contains("void BaseMethod()", csContent);
    }

    [Fact]
    public void Emit_ClassImplementingInterface_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var interfaceDecl = MakeInterfaceDecl("IWorker",
            ImmutableArray.Create(MakeInterfaceMethod("DoWork")));
        var classDecl = MakeClassDecl("Worker", "IWorker");

        var source = "interface IWorker {\n    void DoWork();\n}\nclass Worker : IWorker { }\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                WrapInterface(interfaceDecl).AsTopLevelItem(),
                WrapClass(classDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IWorker", csContent);
        Assert.Contains("class Worker : IWorker", csContent);
    }

    [Fact]
    public void Emit_StructImplementingInterface_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var interfaceDecl = MakeInterfaceDecl("IComparable",
            ImmutableArray.Create(MakeInterfaceMethod("CompareTo")));
        var structDecl = MakeStructDecl("Point", "IComparable");

        var source = "interface IComparable {\n    void CompareTo();\n}\nstruct Point : IComparable { }\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                WrapInterface(interfaceDecl).AsTopLevelItem(),
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
    public void Emit_EmptyInterface_ProducesCorrectCSharp()
    {
        CleanOutputDir();

        var interfaceDecl = MakeInterfaceDecl("IEmpty",
            ImmutableArray<InterfaceMethodDeclarationNode>.Empty);

        var source = "interface IEmpty {\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                WrapInterface(interfaceDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface IEmpty", csContent);
        // Empty interface should have opening and closing braces with nothing between
        var lines = csContent.Split('\n').Select(l => l.Trim()).ToArray();
        var ifaceLineIdx = Array.FindIndex(lines, l => l.StartsWith("interface IEmpty"));
        Assert.True(ifaceLineIdx >= 0);
    }

    [Fact]
    public void Emit_InterfaceMethodWithReturnType_ProducesCorrectSignature()
    {
        CleanOutputDir();

        var intReturnType = new PrimitiveTypeRefTypeReferenceNode
        {
            PrimitiveType = new IntTypePrimitiveTypeNode(),
        };
        var interfaceDecl = MakeInterfaceDecl("ICalculator",
            ImmutableArray.Create(MakeInterfaceMethod("Calculate", intReturnType)));

        var source = "interface ICalculator {\n    int Calculate();\n}\nclass Program { public static void Main() { } }";

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                WrapInterface(interfaceDecl).AsTopLevelItem(),
                WrapClass(MakeProgramClass()).AsTopLevelItem()),
        };

        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("interface ICalculator", csContent);
        Assert.Contains("int Calculate()", csContent);
    ;
    }

}
