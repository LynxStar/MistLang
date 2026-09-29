using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class CallableOverloadRoslynBoundaryTests
{
    [Fact]
    public void ConstructorChainAndHiddenBaseCallsResolveSelectedSymbols()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var boolean = model.Type("Boolean");
        var function = new FunctionType([integer], boolean);
        var baseType = HandBuiltModel.Class("Base");
        var derived = HandBuiltModel.Class("Derived", baseType);
        var selectedConstructor = new Constructor("Base", baseType, [new Parameter("f", function)], new MemberModifiers())
        { DeclaringType = baseType };
        baseType.ConstructorsList.Add(selectedConstructor);
        baseType.ConstructorsList.Add(new Constructor("Base", baseType, [new Parameter("f", model.Type("Predicate"))], new MemberModifiers())
        { DeclaringType = baseType });
        var selectedMethod = new Method("N", integer, [new Parameter("f", function)], new MemberModifiers())
        { DeclaringType = baseType };
        baseType.MethodsList.Add(selectedMethod);
        derived.MethodsList.Add(new Method("N", integer, [new Parameter("f", function)], new MemberModifiers())
        { DeclaringType = derived });
        var argument = new LambdaExpression(HandBuiltModel.Node(), [new LocalVariable("n", integer)],
            HandBuiltModel.Literal(boolean, true), null, false, function);
        var constructor = new Constructor("Derived", derived, [], new MemberModifiers())
        {
            DeclaringType = derived,
            ChainKind = ConstructorChainKind.Base,
            ChainedConstructor = selectedConstructor,
            ChainParameterTypes = [function],
            Body = HandBuiltModel.Block(),
        };
        constructor.ChainArgumentsList.Add(argument);
        var implicitCall = new InvocationExpression(HandBuiltModel.Node(),
            new MethodReferenceExpression(HandBuiltModel.Node(), selectedMethod), [argument], integer, selectedMethod, [function]);
        var caller = new Method("Implicit", integer, [], new MemberModifiers())
        {
            DeclaringType = derived,
            Body = HandBuiltModel.Block(new ReturnStatement(implicitCall, integer)),
        };
        var explicitAccess = new MemberAccessExpression(HandBuiltModel.Node(),
            new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", derived)), "N", integer);
        var explicitCall = new InvocationExpression(HandBuiltModel.Node(), explicitAccess,
            [argument], integer, selectedMethod, [function]);
        var constructorWriter = new CSharpWriter();
        ConstructorEmitter.Emit(constructor, derived, constructorWriter, new DiagnosticBag());
        var callerWriter = new CSharpWriter();
        MethodEmitter.Emit(caller, emitReadonly: false, callerWriter, new DiagnosticBag(), derived);
        var source = $$"""
            namespace Probe;
            public delegate bool Predicate(int n);
            public class Base
            {
                public Base(System.Func<int, bool> f) { }
                public Base(Predicate f) { }
                public int N(System.Func<int, bool> f) => 1;
            }
            public class Derived : Base
            {
                public new int N(System.Func<int, bool> f) => 2;
                {{constructorWriter}}
                {{callerWriter}}
                public int Explicit(Derived value) => {{BodyEmitter.Render(explicitCall, new DiagnosticBag())}};
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = Compile(tree);
        using var stream = new MemoryStream();

        Assert.True(compilation.Emit(stream).Success, string.Join("\n", compilation.GetDiagnostics()));

        var semantic = compilation.GetSemanticModel(tree);
        var initializer = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ConstructorInitializerSyntax>());
        var constructorSymbol = Assert.IsAssignableFrom<IMethodSymbol>(semantic.GetSymbolInfo(initializer).Symbol);
        Assert.Equal("Base", constructorSymbol.ContainingType.Name);
        Assert.Equal("System.Func<int, bool>", constructorSymbol.Parameters[0].Type.ToDisplayString());
        foreach(var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Identifier.Text is "Implicit" or "Explicit"))
        {
            var call = Assert.Single(method.DescendantNodes().OfType<InvocationExpressionSyntax>());
            var symbol = Assert.IsAssignableFrom<IMethodSymbol>(semantic.GetSymbolInfo(call).Symbol);
            Assert.Equal("Base", symbol.ContainingType.Name);
            Assert.Equal("System.Func<int, bool>", symbol.Parameters[0].Type.ToDisplayString());
        }
    }

    [Fact]
    public void EmittedCallableTargetsResolveTheSelectedStructuralOverload()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var boolean = model.Type("Boolean");
        var function = new FunctionType([integer], boolean);
        var pureFunction = new FunctionType([integer], boolean, isPure: true);
        var owner = HandBuiltModel.Class("Probe");
        var selected = new Method("N", model.Type("String"), [new Parameter("f", function)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = owner };
        var predicateDefinition = new ClassType("Predicate<>", "System", new Scope(null), null, null)
        {
            BaseType = new ClassType("MulticastDelegate", "System", new Scope(null), null, null),
        };
        var delegateParameter = new TypeParameter("T", 0, predicateDefinition.Scope, predicateDefinition);
        predicateDefinition.TypeParametersList.Add(delegateParameter);
        predicateDefinition.MethodsList.Add(new Method("Invoke", boolean, [new Parameter("n", delegateParameter)], new MemberModifiers()));
        var predicate = model.Interner.GetOrCreateConstructedType(predicateDefinition, [integer]);
        var competing = new Method("N", model.Type("String"), [new Parameter("f", predicate)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = owner };
        owner.MethodsList.Add(selected);
        owner.MethodsList.Add(competing);
        var pureOwner = HandBuiltModel.Class("PureProbe");
        var selectedPure = new Method("N", model.Type("String"), [new Parameter("f", pureFunction)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = pureOwner };
        var pureCompetitor = new Method("N", model.Type("String"), [new Parameter("f", predicate)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = pureOwner };
        pureOwner.MethodsList.Add(selectedPure);
        pureOwner.MethodsList.Add(pureCompetitor);
        var parameter = new LocalVariable("n", integer);
        var result = HandBuiltModel.Literal(boolean, true);
        var expression = new LambdaExpression(HandBuiltModel.Node(), [parameter], result, null, false, function);
        var block = new LambdaExpression(HandBuiltModel.Node(), [parameter], null,
            HandBuiltModel.Block(new ReturnStatement(result, boolean)), false, pureFunction)
        {
            IsPure = true,
        };
        var check = new Method("Check", boolean, [new Parameter("n", integer)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = owner };
        var group = new FunctionReferenceExpression(HandBuiltModel.Node(), null, check, function);
        var sourceType = HandBuiltModel.Class("Source");
        var instanceCheck = new Method("Check", boolean, [new Parameter("n", integer)], new MemberModifiers())
        {
            DeclaringType = sourceType,
        };
        var instanceGroup = new FunctionReferenceExpression(HandBuiltModel.Node(),
            new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("source", sourceType)), instanceCheck, function);
        var calls = new[]
        {
            RenderCall(selected, expression, function),
            RenderCall(selectedPure, block, pureFunction),
            RenderCall(selected, group, function),
            RenderCall(competing, new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("f", function)), predicate),
            RenderCall(competing, expression, predicate),
            RenderCall(selected, instanceGroup, function),
        };
        var source = $$"""
            public static class Probe
            {
                public static string N(System.Func<int, bool> f) => "func";
                public static string N(System.Predicate<int> f) => "predicate";
                public static bool Check(int n) => n > 1;
                public static string Expression() => {{calls[0]}};
                public static string Block() => {{calls[1]}};
                public static string Group() => {{calls[2]}};
                public static string Typed(System.Func<int, bool> f) => {{calls[3]}};
                public static string NominalLambda() => {{calls[4]}};
                public static string Instance(Source source) => {{calls[5]}};
            }
            public sealed class Source
            {
                public bool Check(int n) => n > 1;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = Compile(tree);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);

        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        var semantic = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text is "Expression" or "Block" or "Group" or "Typed" or "NominalLambda" or "Instance");

        foreach(var method in methods)
        {
            var call = Assert.IsType<InvocationExpressionSyntax>(method.ExpressionBody!.Expression);
            var resolved = Assert.IsAssignableFrom<IMethodSymbol>(semantic.GetSymbolInfo(call).Symbol);
            Assert.Equal(method.Identifier.Text is "Typed" or "NominalLambda"
                ? "System.Predicate<int>" : "System.Func<int, bool>", resolved.Parameters[0].Type.ToDisplayString());
        }

        Assert.Equal(1, calls[5].Split("source.Check", StringSplitOptions.None).Length - 1);

        var bare = CSharpSyntaxTree.ParseText(source.Replace(calls[0], "N((int n) => true)", StringComparison.Ordinal));
        Assert.Contains(Compile(bare).GetDiagnostics(), diagnostic => diagnostic.Id == "CS0121");
    }

    private static string RenderCall(Method selected, Expression argument, IMistType target)
    {
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), selected),
            [argument], selected.ReturnType, selected, [target]);
        return BodyEmitter.Render(call, new DiagnosticBag());
    }

    private static CSharpCompilation Compile(SyntaxTree tree)
    {
        return CSharpCompilation.Create("CallableOverloadBoundary", [tree], CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}