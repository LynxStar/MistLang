// Feature: interfaces, Property 7: Interface method signature mismatch diagnostic
// **Validates: Requirements 7.5**

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying interface method signature mismatch diagnostics.
///
/// Property 7: For any class or struct that implements an interface and provides
/// a method with the correct name but a mismatched return type or parameter types
/// compared to the interface method, the semantic model builder SHALL emit a
/// MIST_SM_0022 diagnostic.
/// </summary>
public class InterfaceSignatureMismatchPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
            Parameters = parameters,
        };
    }

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        return new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    /// <summary>
    /// Creates a MethodDeclarationNode (for implementing types, NOT interface methods).
    /// </summary>
    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
            Parameters = parameters,
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? interfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (interfaceName is not null)
        {
            var _baseList1 = MakeBaseList(interfaceName);

            decl.BaseClause = _baseList1;

            decl.BaseList = _baseList1;
        }

        return decl;
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
            var _ifaceList1 = MakeBaseList(interfaceName);

            decl.InterfaceList = _ifaceList1;

            decl.Interfaces = _ifaceList1;
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Types = typeDecls.ToImmutableArray(),
        };
    }

    private static ParameterListNode MakeParameterList(ParameterNode parameter)
    {
        return new ParameterListNode
        {
            Parameter = parameter,
        };
    }

    private static ParameterNode MakeParameter(string name, TypeReferenceNode type)
    {
        return new ParameterNode
        {
            Name = MakeToken(name),
            Type = type,
        };
    }

    // ─── Primitive type reference helpers ─────────────────────────────────

    private static TypeReferenceNode MakeIntTypeRef() =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() };

    private static TypeReferenceNode MakeStringTypeRef() =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() };

    private static TypeReferenceNode MakeBoolTypeRef() =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new BoolTypePrimitiveTypeNode() };

    private static TypeReferenceNode MakeFloatTypeRef() =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new FloatTypePrimitiveTypeNode() };

    private static TypeReferenceNode MakeVoidTypeRef() =>
        new VoidTypeRefTypeReferenceNode();

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Whether the implementing type is a class or struct.
    /// </summary>
    public enum ImplTypeKind
    {
        Class,
        Struct,
    }

    /// <summary>
    /// A scenario where a type implements an interface and provides a method
    /// with the correct name but a mismatched return type.
    /// </summary>
    public record ReturnTypeMismatchScenario(
        string InterfaceName,
        string MethodName,
        TypeReferenceNode InterfaceReturnType,
        TypeReferenceNode ImplReturnType,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {ImplementingTypeName} : {InterfaceName} {{ {MethodName} return type mismatch }}";
        }
    }

    /// <summary>
    /// A scenario where a type implements an interface and provides a method
    /// with the correct name but mismatched parameter types.
    /// </summary>
    public record ParamTypeMismatchScenario(
        string InterfaceName,
        string MethodName,
        TypeReferenceNode InterfaceParamType,
        TypeReferenceNode ImplParamType,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {ImplementingTypeName} : {InterfaceName} {{ {MethodName} param type mismatch }}";
        }
    }

    /// <summary>
    /// A scenario where a type implements an interface and provides a method
    /// with the correct name but different parameter count (interface has none,
    /// implementing method has parameters, or vice versa).
    /// </summary>
    public record ParamCountMismatchScenario(
        string InterfaceName,
        string MethodName,
        bool InterfaceHasParam,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var ifaceParams = InterfaceHasParam ? "(int)" : "()";
            var implParams = InterfaceHasParam ? "()" : "(int)";
            return $"{kind} {ImplementingTypeName} : {InterfaceName} {{ {MethodName}{ifaceParams} vs {MethodName}{implParams} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] TypeNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    // ─── Type reference pairs for mismatches ─────────────────────────────

    /// <summary>
    /// Pairs of (interfaceReturnType, implReturnType) that are guaranteed to mismatch.
    /// </summary>
    private static readonly (Func<TypeReferenceNode> InterfaceType, Func<TypeReferenceNode> ImplType)[] ReturnTypeMismatchPairs =
    [
        (MakeVoidTypeRef, MakeIntTypeRef),       // void vs int
        (MakeIntTypeRef, MakeStringTypeRef),      // int vs string
        (MakeStringTypeRef, MakeBoolTypeRef),     // string vs bool
        (MakeBoolTypeRef, MakeFloatTypeRef),      // bool vs float
        (MakeIntTypeRef, MakeVoidTypeRef),        // int vs void — not valid for return type in practice but tests the mismatch
        (MakeFloatTypeRef, MakeIntTypeRef),       // float vs int
    ];

    /// <summary>
    /// Pairs of (interfaceParamType, implParamType) that are guaranteed to mismatch.
    /// </summary>
    private static readonly (Func<TypeReferenceNode> InterfaceType, Func<TypeReferenceNode> ImplType)[] ParamTypeMismatchPairs =
    [
        (MakeIntTypeRef, MakeStringTypeRef),      // int vs string
        (MakeStringTypeRef, MakeBoolTypeRef),     // string vs bool
        (MakeBoolTypeRef, MakeIntTypeRef),        // bool vs int
        (MakeFloatTypeRef, MakeStringTypeRef),    // float vs string
        (MakeIntTypeRef, MakeFloatTypeRef),       // int vs float
        (MakeStringTypeRef, MakeIntTypeRef),      // string vs int
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a method has the correct name but a mismatched return type.
    /// </summary>
    public static Gen<ReturnTypeMismatchScenario> GenReturnTypeMismatchScenario()
    {
        return from interfaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from methodIdx in Gen.Choose(0, MethodNamePool.Length - 1)
               from pairIdx in Gen.Choose(0, ReturnTypeMismatchPairs.Length - 1)
               let pair = ReturnTypeMismatchPairs[pairIdx]
               select new ReturnTypeMismatchScenario(
                   InterfaceNamePool[interfaceIdx],
                   MethodNamePool[methodIdx],
                   pair.InterfaceType(),
                   pair.ImplType(),
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    /// <summary>
    /// Generates a scenario where a method has the correct name but mismatched parameter types.
    /// </summary>
    public static Gen<ParamTypeMismatchScenario> GenParamTypeMismatchScenario()
    {
        return from interfaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from methodIdx in Gen.Choose(0, MethodNamePool.Length - 1)
               from pairIdx in Gen.Choose(0, ParamTypeMismatchPairs.Length - 1)
               let pair = ParamTypeMismatchPairs[pairIdx]
               select new ParamTypeMismatchScenario(
                   InterfaceNamePool[interfaceIdx],
                   MethodNamePool[methodIdx],
                   pair.InterfaceType(),
                   pair.ImplType(),
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    /// <summary>
    /// Generates a scenario where a method has the correct name but different parameter count.
    /// </summary>
    public static Gen<ParamCountMismatchScenario> GenParamCountMismatchScenario()
    {
        return from interfaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from methodIdx in Gen.Choose(0, MethodNamePool.Length - 1)
               from interfaceHasParam in Gen.Elements(true, false)
               select new ParamCountMismatchScenario(
                   InterfaceNamePool[interfaceIdx],
                   MethodNamePool[methodIdx],
                   interfaceHasParam,
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    public static Arbitrary<ReturnTypeMismatchScenario> ArbitraryReturnTypeMismatchScenario() =>
        GenReturnTypeMismatchScenario().ToArbitrary();

    public static Arbitrary<ParamTypeMismatchScenario> ArbitraryParamTypeMismatchScenario() =>
        GenParamTypeMismatchScenario().ToArbitrary();

    public static Arbitrary<ParamCountMismatchScenario> ArbitraryParamCountMismatchScenario() =>
        GenParamCountMismatchScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7a: For any class or struct that implements an interface and
    /// provides a method with the correct name but a mismatched return type,
    /// the semantic model builder SHALL emit a MIST_SM_0022
    /// diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceSignatureMismatchPropertyTests) }, MaxTest = 100)]
    public bool ReturnTypeMismatch_EmitsSignatureMismatchDiagnostic(ReturnTypeMismatchScenario scenario)
    {
        // Build the interface with a method that has the specified return type
        var interfaceMethod = MakeInterfaceMethod(
            scenario.MethodName,
            returnType: scenario.InterfaceReturnType);
        var interfaceDecl = MakeInterfaceDecl(
            scenario.InterfaceName,
            ImmutableArray.Create(interfaceMethod));

        // Build the implementing type with a method that has the same name
        // but a different return type
        var implMethod = MakeMethodDecl(
            scenario.MethodName,
            returnType: scenario.ImplReturnType);
        var implMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(implMethod));

        TypeDeclarationNode implTypeDecl;
        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                implTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers));
                break;
            case ImplTypeKind.Struct:
                implTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers));
                break;
            default:
                return false;
        }

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), implTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        // Exactly one signature mismatch diagnostic should be emitted
        if (mismatchDiags.Count != 1)
            return false;

        // The diagnostic message should reference the implementing type and method name
        if (!mismatchDiags[0].Message.Contains(scenario.ImplementingTypeName))
            return false;
        if (!mismatchDiags[0].Message.Contains(scenario.MethodName))
            return false;

        return true;
    }

    /// <summary>
    /// Property 7b: For any class or struct that implements an interface and
    /// provides a method with the correct name but mismatched parameter types,
    /// the semantic model builder SHALL emit a MIST_SM_0022
    /// diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceSignatureMismatchPropertyTests) }, MaxTest = 100)]
    public bool ParamTypeMismatch_EmitsSignatureMismatchDiagnostic(ParamTypeMismatchScenario scenario)
    {
        // Build the interface with a method that has a parameter of the specified type
        var interfaceParamList = MakeParameterList(
            MakeParameter("x", scenario.InterfaceParamType));
        var interfaceMethod = MakeInterfaceMethod(
            scenario.MethodName,
            returnType: MakeVoidTypeRef(),
            parameters: interfaceParamList);
        var interfaceDecl = MakeInterfaceDecl(
            scenario.InterfaceName,
            ImmutableArray.Create(interfaceMethod));

        // Build the implementing type with a method that has the same name
        // but a different parameter type
        var implParamList = MakeParameterList(
            MakeParameter("x", scenario.ImplParamType));
        var implMethod = MakeMethodDecl(
            scenario.MethodName,
            returnType: MakeVoidTypeRef(),
            parameters: implParamList);
        var implMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(implMethod));

        TypeDeclarationNode implTypeDecl;
        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                implTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers));
                break;
            case ImplTypeKind.Struct:
                implTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers));
                break;
            default:
                return false;
        }

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), implTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        // Exactly one signature mismatch diagnostic should be emitted
        if (mismatchDiags.Count != 1)
            return false;

        // The diagnostic message should reference the implementing type and method name
        if (!mismatchDiags[0].Message.Contains(scenario.ImplementingTypeName))
            return false;
        if (!mismatchDiags[0].Message.Contains(scenario.MethodName))
            return false;

        return true;
    }

    /// <summary>
    /// Property 7c: For any class or struct that implements an interface and
    /// provides a method with the correct name but a different parameter count
    /// (interface has no parameters but implementation has one, or vice versa),
    /// the semantic model builder SHALL emit a MIST_SM_0022
    /// diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceSignatureMismatchPropertyTests) }, MaxTest = 100)]
    public bool ParamCountMismatch_EmitsSignatureMismatchDiagnostic(ParamCountMismatchScenario scenario)
    {
        InterfaceMethodDeclarationNode interfaceMethod;
        MethodDeclarationNode implMethod;

        if (scenario.InterfaceHasParam)
        {
            // Interface method has a parameter, implementing method has none
            var interfaceParamList = MakeParameterList(
                MakeParameter("x", MakeIntTypeRef()));
            interfaceMethod = MakeInterfaceMethod(
                scenario.MethodName,
                returnType: MakeVoidTypeRef(),
                parameters: interfaceParamList);
            implMethod = MakeMethodDecl(
                scenario.MethodName,
                returnType: MakeVoidTypeRef());
        }
        else
        {
            // Interface method has no parameters, implementing method has one
            interfaceMethod = MakeInterfaceMethod(
                scenario.MethodName,
                returnType: MakeVoidTypeRef());
            var implParamList = MakeParameterList(
                MakeParameter("x", MakeIntTypeRef()));
            implMethod = MakeMethodDecl(
                scenario.MethodName,
                returnType: MakeVoidTypeRef(),
                parameters: implParamList);
        }

        var interfaceDecl = MakeInterfaceDecl(
            scenario.InterfaceName,
            ImmutableArray.Create(interfaceMethod));

        var implMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(implMethod));

        TypeDeclarationNode implTypeDecl;
        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                implTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers));
                break;
            case ImplTypeKind.Struct:
                implTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers));
                break;
            default:
                return false;
        }

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), implTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        // Exactly one signature mismatch diagnostic should be emitted
        if (mismatchDiags.Count != 1)
            return false;

        // The diagnostic message should reference the implementing type and method name
        if (!mismatchDiags[0].Message.Contains(scenario.ImplementingTypeName))
            return false;
        if (!mismatchDiags[0].Message.Contains(scenario.MethodName))
            return false;

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassMethod_VoidVsInt_ReturnTypeMismatch_EmitsDiagnostic()
    {
        // Interface method returns void, class method returns int
        var interfaceMethod = MakeInterfaceMethod("DoWork", returnType: MakeVoidTypeRef());
        var interfaceDecl = MakeInterfaceDecl("IWorker", ImmutableArray.Create(interfaceMethod));

        var classMethod = MakeMethodDecl("DoWork", returnType: MakeIntTypeRef());
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Worker", "IWorker", classMembers);

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        Assert.Single(mismatchDiags);
        Assert.Contains("Worker", mismatchDiags[0].Message);
        Assert.Contains("DoWork", mismatchDiags[0].Message);
        Assert.Contains("IWorker", mismatchDiags[0].Message);
    }

    [Fact]
    public void ClassMethod_IntVsString_ReturnTypeMismatch_EmitsDiagnostic()
    {
        // Interface method returns int, class method returns string
        var interfaceMethod = MakeInterfaceMethod("Calculate", returnType: MakeIntTypeRef());
        var interfaceDecl = MakeInterfaceDecl("ICalculator", ImmutableArray.Create(interfaceMethod));

        var classMethod = MakeMethodDecl("Calculate", returnType: MakeStringTypeRef());
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Calculator", "ICalculator", classMembers);

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        Assert.Single(mismatchDiags);
        Assert.Contains("Calculator", mismatchDiags[0].Message);
        Assert.Contains("Calculate", mismatchDiags[0].Message);
    }

    [Fact]
    public void ClassMethod_ParamTypeMismatch_IntVsString_EmitsDiagnostic()
    {
        // Interface method has int parameter, class method has string parameter
        var interfaceParamList = MakeParameterList(MakeParameter("x", MakeIntTypeRef()));
        var interfaceMethod = MakeInterfaceMethod("Process", returnType: MakeVoidTypeRef(), parameters: interfaceParamList);
        var interfaceDecl = MakeInterfaceDecl("IProcessor", ImmutableArray.Create(interfaceMethod));

        var implParamList = MakeParameterList(MakeParameter("x", MakeStringTypeRef()));
        var classMethod = MakeMethodDecl("Process", returnType: MakeVoidTypeRef(), parameters: implParamList);
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Processor", "IProcessor", classMembers);

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        Assert.Single(mismatchDiags);
        Assert.Contains("Processor", mismatchDiags[0].Message);
        Assert.Contains("Process", mismatchDiags[0].Message);
    }

    [Fact]
    public void ClassMethod_NoParamsVsHasParams_EmitsDiagnostic()
    {
        // Interface method has no parameters, class method has a parameter
        var interfaceMethod = MakeInterfaceMethod("Run", returnType: MakeVoidTypeRef());
        var interfaceDecl = MakeInterfaceDecl("IRunner", ImmutableArray.Create(interfaceMethod));

        var implParamList = MakeParameterList(MakeParameter("x", MakeIntTypeRef()));
        var classMethod = MakeMethodDecl("Run", returnType: MakeVoidTypeRef(), parameters: implParamList);
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Runner", "IRunner", classMembers);

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        Assert.Single(mismatchDiags);
        Assert.Contains("Runner", mismatchDiags[0].Message);
        Assert.Contains("Run", mismatchDiags[0].Message);
    }

    [Fact]
    public void StructMethod_ReturnTypeMismatch_EmitsDiagnostic()
    {
        // Struct implementing interface with return type mismatch
        var interfaceMethod = MakeInterfaceMethod("GetValue", returnType: MakeIntTypeRef());
        var interfaceDecl = MakeInterfaceDecl("IValueProvider", ImmutableArray.Create(interfaceMethod));

        var structMethod = MakeMethodDecl("GetValue", returnType: MakeStringTypeRef());
        var structMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(structMethod));
        var structDecl = MakeStructDecl("ValueHolder", "IValueProvider", structMembers);

        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl), WrapStruct(structDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        Assert.Single(mismatchDiags);
        Assert.Contains("ValueHolder", mismatchDiags[0].Message);
        Assert.Contains("GetValue", mismatchDiags[0].Message);
    }

}
