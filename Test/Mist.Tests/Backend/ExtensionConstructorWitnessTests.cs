using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ExtensionConstructorWitnessTests
{
    private readonly GenericDefaultShapes shapes = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtensionDeclaration_ReceivesTheFactoryBeforeItsUserParameters(bool parameterized)
    {
        var method = Method(parameterized);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        ExtensionBlockEmitter.Emit(method.DeclaringBlock!, writer, diagnostics, ref anyEmitted);

        var actual = writer.ToString();
        Assert.Contains(parameterized
            ? "public static T Make<T>(this Holder self, System.Func<string, T> __construct_T_0, string text)"
            : "public static T Fresh<T>(this Holder self, System.Func<T> __construct_T_0)", actual, StringComparison.Ordinal);
        Assert.Contains(parameterized ? "return __construct_T_0(text);" : "return __construct_T_0();", actual, StringComparison.Ordinal);
        Assert.Contains(parameterized ? "where T : Mist.Emission.CSharp.__IConstruct_string<T>" : "where T : new()", actual, StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtensionInvocation_SeparatesItsReceiverFactoryAndUserArguments(bool parameterized)
    {
        var method = Method(parameterized);
        var product = shapes.Type("Product");
        _ = Constructor(product, parameterized ? [shapes.String] : []);
        var receiver = Receiver(method.DeclaringBlock!.ResolvedReceiverType!);
        var member = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, product, [product]);
        Expression[] arguments = parameterized ? [HandBuiltModel.Literal(shapes.String, "chosen")] : [];
        var call = new InvocationExpression(HandBuiltModel.Node(), member, arguments, product, method,
            method.ParametersList.Select(parameter => parameter.Type).ToList());

        Assert.Equal(parameterized
            ? "Probe.HolderExtensions.Make<Product>(self, (__arg0_0) => new Product(__arg0_0), \"chosen\")"
            : "Probe.HolderExtensions.Fresh<Product>(self, () => new Product())", Emit(call));
    }

    [Fact]
    public void ExtensionDeclarationAndCall_PlaceDefaultBeforeConstructorBeforeUserArgument()
    {
        var method = Method(parameterized: true, defaultConstrained: true);
        var product = shapes.Type("Product");
        _ = Constructor(product, shapes.String);
        Block(product);
        var receiver = Receiver(method.DeclaringBlock!.ResolvedReceiverType!);
        var member = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, product, [product]);
        var call = new InvocationExpression(HandBuiltModel.Node(), member, [HandBuiltModel.Literal(shapes.String, "chosen")], product, method, [shapes.String]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        ExtensionBlockEmitter.Emit(method.DeclaringBlock, writer, diagnostics, ref anyEmitted);

        Assert.Contains("this Holder self, System.Func<T> __default_T, System.Func<string, T> __construct_T_0, string text", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal("Probe.HolderExtensions.Make<Product>(self, () => Product.__CreateDefault(), (__arg0_0) => new Product(__arg0_0), \"chosen\")", Emit(call));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ExtensionMethodGroup_PreservesHoistedBlockArgumentsAndWitnessOrdering()
    {
        var method = Method(parameterized: true, defaultConstrained: true, genericBlock: true);
        var product = shapes.Type("Product");
        _ = Constructor(product, shapes.String);
        Block(product);
        var holder = Assert.IsType<ConstructedType>(method.DeclaringBlock!.ResolvedReceiverType!).Definition;
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), Receiver(Construct(holder, shapes.Integer)), method,
            new FunctionType([shapes.String], product))
        { TypeArguments = [product] };

        var actual = Emit(reference);

        Assert.Contains("Probe.HolderExtensions.Make<int, Product>(__receiver, () => Product.__CreateDefault(), (__arg0_0) => new Product(__arg0_0), __arg0)", actual, StringComparison.Ordinal);
        Assert.EndsWith(")(self)", actual, StringComparison.Ordinal);
    }

    private Method Method(bool parameterized, bool defaultConstrained = false, bool genericBlock = false)
    {
        var scope = new Scope(null);
        var holder = shapes.Type("Holder");
        var block = new ExtensionBlock("self", "Probe", scope, DeclarationFragments.At<ExtensionBlockDeclarationNode>())
        {
            CarrierName = "HolderExtensions",
            ResolvedReceiverType = holder,
        };
        var closing = TypeSubstitution.Empty;

        if(genericBlock)
        {
            var held = Parameter(holder, "B");
            block.TypeParametersList.Add(held);
            block.ResolvedReceiverType = Construct(holder, held);
            closing = TypeSubstitution.ForParameters([held], [shapes.Integer]);
        }

        var parameter = new TypeParameter("T", 0, scope, null);
        IReadOnlyList<IMistType> signature = parameterized ? [shapes.String] : [];
        parameter.ConstraintsList.Add(new ConstructorConstraint(signature));

        if(defaultConstrained)
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        }

        var text = new Parameter("text", shapes.String);
        Expression[] arguments = parameterized ? [new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(text.Name, text.Type))] : [];
        var method = new Method(parameterized ? "Make" : "Fresh", parameter, parameterized ? [text] : [],
            new MemberModifiers { Accessibility = Accessibility.Public })
        {
            Scope = scope,
            DeclaringBlock = block,
            ClosedBlock = block,
            BlockClosing = closing,
            Body = HandBuiltModel.Block(new ReturnStatement(new NewExpression(HandBuiltModel.Node(), parameter, arguments, null, [], signature), parameter)),
        };
        method.TypeParametersList.Add(parameter);
        block.MethodsList.Add(method);
        return method;
    }

    private static LocalReferenceExpression Receiver(IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("self", type));
    }

    private static string Emit(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }
}