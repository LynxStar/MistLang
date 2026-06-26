// Feature: abstracts, Property 7: Abstract method signature mismatch diagnostic
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
/// Property-based tests verifying abstract method signature mismatch diagnostics.
///
/// Property 7: For any class or struct that implements an abstract and provides
/// a method with the correct name but a mismatched return type or parameter types
/// compared to the abstract method, the semantic model builder SHALL emit a
/// MIST_SM_0022 diagnostic.
/// </summary>
public class AbstractSignatureMismatchPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

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
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static AbstractMethodDeclarationNode MakeAbstractMethod(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new AbstractMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
        };
    }

    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<AbstractMethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (AbstractMemberDeclarationNode)new AbstractMethodMemberAbstractMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        return new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    /// <summary>
    /// Creates a MethodDeclarationNode (for implementing types, NOT abstract methods).
    /// </summary>
    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? abstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(abstractName is not null)
        {
            var _baseList1 = MakeBaseList(abstractName);

            decl.BaseClause = _baseList1;

            decl.BaseList = _baseList1;
        }

        return decl;
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
            var _abstractList1 = MakeBaseList(abstractName);

            decl.AbstractList = _abstractList1;

            decl.Abstracts = _abstractList1;
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method)
    {
        return new MethodMemberMemberDeclarationNode { Method = method };
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

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
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

    private static TypeReferenceNode MakeIntTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } };
    }

    private static TypeReferenceNode MakeStringTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } };
    }

    private static TypeReferenceNode MakeBoolTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } };
    }

    private static TypeReferenceNode MakeFloatTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new FloatTypePrimitiveTypeNode() } };
    }

    private static TypeReferenceNode MakeVoidTypeRef()
    {
        return new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() };
    }

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
    /// A scenario where a type implements an abstract and provides a method
    /// with the correct name but a mismatched return type.
    /// </summary>
    public record ReturnTypeMismatchScenario(
        string AbstractName,
        string MethodName,
        TypeReferenceNode AbstractReturnType,
        TypeReferenceNode ImplReturnType,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {ImplementingTypeName} : {AbstractName} {{ {MethodName} return type mismatch }}";
        }
    }

    /// <summary>
    /// A scenario where a type implements an abstract and provides a method
    /// with the correct name but mismatched parameter types.
    /// </summary>
    public record ParamTypeMismatchScenario(
        string AbstractName,
        string MethodName,
        TypeReferenceNode AbstractParamType,
        TypeReferenceNode ImplParamType,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {ImplementingTypeName} : {AbstractName} {{ {MethodName} param type mismatch }}";
        }
    }

    /// <summary>
    /// A scenario where a type implements an abstract and provides a method
    /// with the correct name but different parameter count (abstract has none,
    /// implementing method has parameters, or vice versa).
    /// </summary>
    public record ParamCountMismatchScenario(
        string AbstractName,
        string MethodName,
        bool AbstractHasParam,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var abstractParams = AbstractHasParam ? "(int)" : "()";
            var implParams = AbstractHasParam ? "()" : "(int)";
            return $"{kind} {ImplementingTypeName} : {AbstractName} {{ {MethodName}{abstractParams} vs {MethodName}{implParams} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] TypeNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    // ─── Type reference pairs for mismatches ─────────────────────────────

    /// <summary>
    /// Pairs of (abstractReturnType, implReturnType) that are guaranteed to mismatch.
    /// </summary>
    private static readonly (Func<TypeReferenceNode> AbstractType, Func<TypeReferenceNode> ImplType)[] ReturnTypeMismatchPairs =
    [
        (MakeVoidTypeRef, MakeIntTypeRef),       // void vs int
        (MakeIntTypeRef, MakeStringTypeRef),      // int vs string
        (MakeStringTypeRef, MakeBoolTypeRef),     // string vs bool
        (MakeBoolTypeRef, MakeFloatTypeRef),      // bool vs float
        (MakeIntTypeRef, MakeVoidTypeRef),        // int vs void — not valid for return type in practice but tests the mismatch
        (MakeFloatTypeRef, MakeIntTypeRef),       // float vs int
    ];

    /// <summary>
    /// Pairs of (abstractParamType, implParamType) that are guaranteed to mismatch.
    /// </summary>
    private static readonly (Func<TypeReferenceNode> AbstractType, Func<TypeReferenceNode> ImplType)[] ParamTypeMismatchPairs =
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
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from methodIdx in Gen.Choose(0, MethodNamePool.Length - 1)
               from pairIdx in Gen.Choose(0, ReturnTypeMismatchPairs.Length - 1)
               let pair = ReturnTypeMismatchPairs[pairIdx]
               select new ReturnTypeMismatchScenario(
                   AbstractNamePool[abstractIdx],
                   MethodNamePool[methodIdx],
                   pair.AbstractType(),
                   pair.ImplType(),
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    /// <summary>
    /// Generates a scenario where a method has the correct name but mismatched parameter types.
    /// </summary>
    public static Gen<ParamTypeMismatchScenario> GenParamTypeMismatchScenario()
    {
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from methodIdx in Gen.Choose(0, MethodNamePool.Length - 1)
               from pairIdx in Gen.Choose(0, ParamTypeMismatchPairs.Length - 1)
               let pair = ParamTypeMismatchPairs[pairIdx]
               select new ParamTypeMismatchScenario(
                   AbstractNamePool[abstractIdx],
                   MethodNamePool[methodIdx],
                   pair.AbstractType(),
                   pair.ImplType(),
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    /// <summary>
    /// Generates a scenario where a method has the correct name but different parameter count.
    /// </summary>
    public static Gen<ParamCountMismatchScenario> GenParamCountMismatchScenario()
    {
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from methodIdx in Gen.Choose(0, MethodNamePool.Length - 1)
               from abstractHasParam in Gen.Elements(true, false)
               select new ParamCountMismatchScenario(
                   AbstractNamePool[abstractIdx],
                   MethodNamePool[methodIdx],
                   abstractHasParam,
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    public static Arbitrary<ReturnTypeMismatchScenario> ArbitraryReturnTypeMismatchScenario()
    {
        return GenReturnTypeMismatchScenario().ToArbitrary();
    }

    public static Arbitrary<ParamTypeMismatchScenario> ArbitraryParamTypeMismatchScenario()
    {
        return GenParamTypeMismatchScenario().ToArbitrary();
    }

    public static Arbitrary<ParamCountMismatchScenario> ArbitraryParamCountMismatchScenario()
    {
        return GenParamCountMismatchScenario().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7a: For any class or struct that implements an abstract and
    /// provides a method with the correct name but a mismatched return type,
    /// the semantic model builder SHALL emit a MIST_SM_0022
    /// diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractSignatureMismatchPropertyTests) }, MaxTest = 100)]
    public bool ReturnTypeMismatch_EmitsSignatureMismatchDiagnostic(ReturnTypeMismatchScenario scenario)
    {
        // Build the abstract with a method that has the specified return type
        var abstractMethod = MakeAbstractMethod(
            scenario.MethodName,
            returnType: scenario.AbstractReturnType);
        var abstractDecl = MakeAbstractDecl(
            scenario.AbstractName,
            ImmutableArray.Create(abstractMethod));

        // Build the implementing type with a method that has the same name
        // but a different return type
        var implMethod = MakeMethodDecl(
            scenario.MethodName,
            returnType: scenario.ImplReturnType);
        var implMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(implMethod));

        TypeDeclarationNode implTypeDecl;
        switch(scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                implTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers));
                break;
            case ImplTypeKind.Struct:
                implTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers));
                break;
            default:
                return false;
        }

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), implTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        // Exactly one signature mismatch diagnostic should be emitted
        if(mismatchDiags.Count != 1)
        {
            return false;
        }

        // The diagnostic message should reference the implementing type and method name
        if(!mismatchDiags[0].Message.Contains(scenario.ImplementingTypeName))
        {
            return false;
        }

        if(!mismatchDiags[0].Message.Contains(scenario.MethodName))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Property 7b: For any class or struct that implements an abstract and
    /// provides a method with the correct name but mismatched parameter types,
    /// the semantic model builder SHALL emit a MIST_SM_0022
    /// diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractSignatureMismatchPropertyTests) }, MaxTest = 100)]
    public bool ParamTypeMismatch_EmitsSignatureMismatchDiagnostic(ParamTypeMismatchScenario scenario)
    {
        // Build the abstract with a method that has a parameter of the specified type
        var abstractParamList = MakeParameterList(
            MakeParameter("x", scenario.AbstractParamType));
        var abstractMethod = MakeAbstractMethod(
            scenario.MethodName,
            returnType: MakeVoidTypeRef(),
            parameters: abstractParamList);
        var abstractDecl = MakeAbstractDecl(
            scenario.AbstractName,
            ImmutableArray.Create(abstractMethod));

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
        switch(scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                implTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers));
                break;
            case ImplTypeKind.Struct:
                implTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers));
                break;
            default:
                return false;
        }

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), implTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        // Exactly one signature mismatch diagnostic should be emitted
        if(mismatchDiags.Count != 1)
        {
            return false;
        }

        // The diagnostic message should reference the implementing type and method name
        if(!mismatchDiags[0].Message.Contains(scenario.ImplementingTypeName))
        {
            return false;
        }

        if(!mismatchDiags[0].Message.Contains(scenario.MethodName))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Property 7c: For any class or struct that implements an abstract and
    /// provides a method with the correct name but a different parameter count
    /// (abstract has no parameters but implementation has one, or vice versa),
    /// the semantic model builder SHALL emit a MIST_SM_0022
    /// diagnostic.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractSignatureMismatchPropertyTests) }, MaxTest = 100)]
    public bool ParamCountMismatch_EmitsSignatureMismatchDiagnostic(ParamCountMismatchScenario scenario)
    {
        AbstractMethodDeclarationNode abstractMethod;
        MethodDeclarationNode implMethod;

        if(scenario.AbstractHasParam)
        {
            // Abstract method has a parameter, implementing method has none
            var abstractParamList = MakeParameterList(
                MakeParameter("x", MakeIntTypeRef()));
            abstractMethod = MakeAbstractMethod(
                scenario.MethodName,
                returnType: MakeVoidTypeRef(),
                parameters: abstractParamList);
            implMethod = MakeMethodDecl(
                scenario.MethodName,
                returnType: MakeVoidTypeRef());
        }
        else
        {
            // Abstract method has no parameters, implementing method has one
            abstractMethod = MakeAbstractMethod(
                scenario.MethodName,
                returnType: MakeVoidTypeRef());
            var implParamList = MakeParameterList(
                MakeParameter("x", MakeIntTypeRef()));
            implMethod = MakeMethodDecl(
                scenario.MethodName,
                returnType: MakeVoidTypeRef(),
                parameters: implParamList);
        }

        var abstractDecl = MakeAbstractDecl(
            scenario.AbstractName,
            ImmutableArray.Create(abstractMethod));

        var implMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(implMethod));

        TypeDeclarationNode implTypeDecl;
        switch(scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                implTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers));
                break;
            case ImplTypeKind.Struct:
                implTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers));
                break;
            default:
                return false;
        }

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), implTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        // Exactly one signature mismatch diagnostic should be emitted
        if(mismatchDiags.Count != 1)
        {
            return false;
        }

        // The diagnostic message should reference the implementing type and method name
        if(!mismatchDiags[0].Message.Contains(scenario.ImplementingTypeName))
        {
            return false;
        }

        if(!mismatchDiags[0].Message.Contains(scenario.MethodName))
        {
            return false;
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassMethod_VoidVsInt_ReturnTypeMismatch_EmitsDiagnostic()
    {
        // Abstract method returns void, class method returns int
        var abstractMethod = MakeAbstractMethod("DoWork", returnType: MakeVoidTypeRef());
        var abstractDecl = MakeAbstractDecl("IWorker", ImmutableArray.Create(abstractMethod));

        var classMethod = MakeMethodDecl("DoWork", returnType: MakeIntTypeRef());
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Worker", "IWorker", classMembers);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        _ = Assert.Single(mismatchDiags);
        Assert.Contains("Worker", mismatchDiags[0].Message);
        Assert.Contains("DoWork", mismatchDiags[0].Message);
        Assert.Contains("IWorker", mismatchDiags[0].Message);
    }

    [Fact]
    public void ClassMethod_IntVsString_ReturnTypeMismatch_EmitsDiagnostic()
    {
        // Abstract method returns int, class method returns string
        var abstractMethod = MakeAbstractMethod("Calculate", returnType: MakeIntTypeRef());
        var abstractDecl = MakeAbstractDecl("ICalculator", ImmutableArray.Create(abstractMethod));

        var classMethod = MakeMethodDecl("Calculate", returnType: MakeStringTypeRef());
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Calculator", "ICalculator", classMembers);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        _ = Assert.Single(mismatchDiags);
        Assert.Contains("Calculator", mismatchDiags[0].Message);
        Assert.Contains("Calculate", mismatchDiags[0].Message);
    }

    [Fact]
    public void ClassMethod_ParamTypeMismatch_IntVsString_EmitsDiagnostic()
    {
        // Abstract method has int parameter, class method has string parameter
        var abstractParamList = MakeParameterList(MakeParameter("x", MakeIntTypeRef()));
        var abstractMethod = MakeAbstractMethod("Process", returnType: MakeVoidTypeRef(), parameters: abstractParamList);
        var abstractDecl = MakeAbstractDecl("IProcessor", ImmutableArray.Create(abstractMethod));

        var implParamList = MakeParameterList(MakeParameter("x", MakeStringTypeRef()));
        var classMethod = MakeMethodDecl("Process", returnType: MakeVoidTypeRef(), parameters: implParamList);
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Processor", "IProcessor", classMembers);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        _ = Assert.Single(mismatchDiags);
        Assert.Contains("Processor", mismatchDiags[0].Message);
        Assert.Contains("Process", mismatchDiags[0].Message);
    }

    [Fact]
    public void ClassMethod_NoParamsVsHasParams_EmitsDiagnostic()
    {
        // Abstract method has no parameters, class method has a parameter
        var abstractMethod = MakeAbstractMethod("Run", returnType: MakeVoidTypeRef());
        var abstractDecl = MakeAbstractDecl("IRunner", ImmutableArray.Create(abstractMethod));

        var implParamList = MakeParameterList(MakeParameter("x", MakeIntTypeRef()));
        var classMethod = MakeMethodDecl("Run", returnType: MakeVoidTypeRef(), parameters: implParamList);
        var classMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(classMethod));
        var classDecl = MakeClassDecl("Runner", "IRunner", classMembers);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapClass(classDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        _ = Assert.Single(mismatchDiags);
        Assert.Contains("Runner", mismatchDiags[0].Message);
        Assert.Contains("Run", mismatchDiags[0].Message);
    }

    [Fact]
    public void StructMethod_ReturnTypeMismatch_EmitsDiagnostic()
    {
        // Struct implementing abstract with return type mismatch
        var abstractMethod = MakeAbstractMethod("GetValue", returnType: MakeIntTypeRef());
        var abstractDecl = MakeAbstractDecl("IValueProvider", ImmutableArray.Create(abstractMethod));

        var structMethod = MakeMethodDecl("GetValue", returnType: MakeStringTypeRef());
        var structMembers = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(structMethod));
        var structDecl = MakeStructDecl("ValueHolder", "IValueProvider", structMembers);

        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl), WrapStruct(structDecl));
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();
        _ = Assert.Single(mismatchDiags);
        Assert.Contains("ValueHolder", mismatchDiags[0].Message);
        Assert.Contains("GetValue", mismatchDiags[0].Message);
    }

}