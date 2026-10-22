using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class KeywordIdentifierEmissionTests
{
    private static readonly MemberModifiers Public = DeclarationFragments.Public;
    private static readonly MemberModifiers PublicStatic = new() { Accessibility = Accessibility.Public, IsStatic = true };
    private readonly HandBuiltModel model = new();

    private ClassType Integer => model.Type("Int32");

    [Fact]
    public void ALocalIsVerbatimWhereDeclaredAndRead()
    {
        var local = new LocalVariable("fixed", Integer);
        var method = Method("Main", Integer, [],
            new LocalDeclarationStatement(local, HandBuiltModel.Literal(Integer, 3L)),
            new ReturnStatement(Read(local), Integer));

        var text = EmitMethod(method);

        Assert.Contains("int @fixed = 3;", text, StringComparison.Ordinal);
        Assert.Contains("return @fixed;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterIsVerbatimWhereDeclaredAndRead()
    {
        var parameter = new Parameter("lock", Integer);
        var method = Method("Twice", Integer, [parameter],
            new ReturnStatement(Read(new LocalVariable("lock", Integer) { Parameter = parameter }), Integer));

        var text = EmitMethod(method);

        Assert.Contains("int Twice(int @lock)", text, StringComparison.Ordinal);
        Assert.Contains("return @lock;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodIsVerbatimWhereDeclaredAndCalled()
    {
        var callee = Method("goto", Integer, [], new ReturnStatement(HandBuiltModel.Literal(Integer, 6L), Integer));
        var caller = Method("Main", Integer, [],
            new ReturnStatement(new InvocationExpression(HandBuiltModel.Node(),
                new MethodReferenceExpression(HandBuiltModel.Node(), callee), [], Integer, callee, null), Integer));

        Assert.Contains("static int @goto()", EmitMethod(callee), StringComparison.Ordinal);
        Assert.Contains("return @goto();", EmitMethod(caller), StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalFunctionAndItsParameterAreVerbatimWhereDeclaredAndUsed()
    {
        var parameter = new Parameter("sizeof", Integer);
        var parameterLocal = new LocalVariable("sizeof", Integer) { Parameter = parameter };
        var function = new Method("override", Integer, [parameter], default)
        {
            Body = HandBuiltModel.Block(new ReturnStatement(Read(parameterLocal), Integer)),
        };
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), function),
            [HandBuiltModel.Literal(Integer, 1L)], Integer, function, null);
        var method = Method("Main", Integer, [],
            new LocalFunctionStatement(function, [parameterLocal]),
            new ReturnStatement(call, Integer));

        var text = EmitMethod(method);

        Assert.Contains("int @override(int @sizeof)", text, StringComparison.Ordinal);
        Assert.Contains("return @sizeof;", text, StringComparison.Ordinal);
        Assert.Contains("return @override(1);", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeAndItsMembersAreVerbatimWhereDeclaredAndReferenced()
    {
        var type = HandBuiltModel.Class("event");
        var field = new Field("checked", Integer, OwnershipKind.Unrestricted, Public) { DeclaringType = type };
        var property = new Property("virtual", Integer, Public) { Getter = new PropertyAccessor { IsAuto = true }, DeclaringType = type };
        type.FieldsList.Add(field);
        type.PropertiesList.Add(property);
        type.MethodsList.Add(new Method("volatile", Integer, [], Public)
        {
            DeclaringType = type,
            Body = HandBuiltModel.Block(new ReturnStatement(new BinaryExpression(HandBuiltModel.Node(),
                new FieldReferenceExpression(HandBuiltModel.Node(), field), BinaryOperatorKind.Add,
                new PropertyReferenceExpression(HandBuiltModel.Node(), property), Integer), Integer)),
        });

        var text = EmitType(type);

        Assert.Contains("public class @event", text, StringComparison.Ordinal);
        Assert.Contains("public @event()", text, StringComparison.Ordinal);
        Assert.Contains("public int @checked;", text, StringComparison.Ordinal);
        Assert.Contains("public int @virtual { get; }", text, StringComparison.Ordinal);
        Assert.Contains("public int @volatile()", text, StringComparison.Ordinal);
        Assert.Contains("return this.@checked + @virtual;", text, StringComparison.Ordinal);
        Assert.Equal("@event", TypeNameEmitter.Emit(type));
    }

    [Fact]
    public void AMemberReachedThroughAReceiverIsVerbatim()
    {
        var type = HandBuiltModel.Class("Box");
        var box = new LocalVariable("box", type);

        Assert.Equal("box.@checked", EmitExpression(new MemberAccessExpression(HandBuiltModel.Node(), Read(box), "checked", Integer)));
    }

    [Fact]
    public void ATypeParameterIsVerbatimWhereDeclaredAndUsed()
    {
        var type = HandBuiltModel.Class("Box");
        var parameter = new TypeParameter("delegate", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        type.FieldsList.Add(new Field("Item", parameter, OwnershipKind.Unrestricted, Public) { DeclaringType = type });

        var text = EmitType(type);

        Assert.Contains("public class Box<@delegate>", text, StringComparison.Ordinal);
        Assert.Contains("public @delegate Item;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumMemberIsVerbatimWhereDeclaredAndReadButPrintsAsWritten()
    {
        var mode = HandBuiltModel.Enum("Mode", Integer);
        mode.MembersList.Add(new EnumMember("lock", isDefault: false, HandBuiltModel.Literal(Integer, 1L)));
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        EnumEmitter.Emit(mode, writer, diagnostics);

        var text = writer.ToString();
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Contains("public static readonly Mode @lock = new Mode(1);", text, StringComparison.Ordinal);
        Assert.Contains("if(ReferenceEquals(this, Mode.@lock)) return \"lock\";", text, StringComparison.Ordinal);
        Assert.Equal("Mode.@lock", EmitExpression(new MemberAccessExpression(HandBuiltModel.Node(),
            new TypeReferenceExpression(HandBuiltModel.Node(), mode), "lock", mode)));
    }

    [Fact]
    public void AnAttributesNamedArgumentIsVerbatim()
    {
        var attribute = new DeclaredAttribute(HandBuiltModel.Class("Marker"), [], [("fixed", HandBuiltModel.Literal(Integer, 1L))]);

        Assert.Equal("[Marker(@fixed = 1)] ", AttributeEmitter.Inline([attribute], new DiagnosticBag()));
    }

    [Fact]
    public void AnAnonymousTypesMemberIsVerbatimWhereDeclaredAndRead()
    {
        var anonymous = new AnonymousType(["do"], [Integer]);

        Assert.Equal("new { @do = 10 }", EmitExpression(new AnonymousObjectExpression(HandBuiltModel.Node(),
            [HandBuiltModel.Literal(Integer, 10L)], anonymous)));
        Assert.Equal("anon.@do", EmitExpression(new MemberAccessExpression(HandBuiltModel.Node(),
            Read(new LocalVariable("anon", anonymous)), "do", Integer)));
    }

    [Fact]
    public void APatternsMemberAndBindingAreVerbatim()
    {
        var type = HandBuiltModel.Class("Box");
        var binding = new LocalVariable("case", Integer);
        var pattern = new PropertyPattern(HandBuiltModel.Node(), type,
            [new PropertySubpattern(HandBuiltModel.Node(), "extern", new CatchAllPattern(HandBuiltModel.Node(), Integer, binding), Integer, new NotFoundResult())]);

        Assert.Equal("box is { @extern: var @case }", EmitExpression(new IsPatternExpression(HandBuiltModel.Node(),
            Read(new LocalVariable("box", type)), pattern, model.Type("Boolean"))));
    }

    [Fact]
    public void ADefaultBindingsFieldAndNestedOverrideAreVerbatim()
    {
        var square = HandBuiltModel.Class("Sq");
        square.FieldsList.Add(new Field("checked", Integer, OwnershipKind.Unrestricted, Public) { DeclaringType = square });
        var holder = HandBuiltModel.Class("Holder");
        holder.FieldsList.Add(new Field("lock", square, OwnershipKind.Unrestricted, Public) { DeclaringType = holder });
        holder.DefaultBlock = new DefaultBlock(
            [new DefaultBindingEntry("lock", square, [new DefaultValueEntry("checked", HandBuiltModel.Literal(Integer, 15L))],
                new DefaultTypeBindingDefaultFieldInitializerNode())],
            new DefaultBlockDeclarationNode());
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        DefaultEmitter.Emit(holder, writer, diagnostics, ref anyEmitted);

        var text = writer.ToString();
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Contains("__default_lock.@checked = 15;", text, StringComparison.Ordinal);
        Assert.Contains("this.@lock = __default_lock;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamespaceSegmentIsVerbatimWhereATypeIsQualifiedByIt()
    {
        var helper = new ClassType("Helper<>", "HelloMist.unsafe", new Scope(null), null, null);
        helper.TypeParametersList.Add(new TypeParameter("T", 0, helper.Scope, helper));

        Assert.Equal("HelloMist.@unsafe.Helper<int>", TypeNameEmitter.Emit(new ConstructedType(helper, [Integer])));
    }

    [Fact]
    public void AnExtensionCarriersNamespaceSegmentIsVerbatimWhereItsMethodIsCalled()
    {
        var greeting = new ClassType("Greeting", "HelloMist.unsafe", new Scope(null), null, null);
        var block = new ExtensionBlock("this2", "HelloMist.unsafe", new Scope(null), new ExtensionBlockDeclarationNode())
        { ResolvedReceiverType = greeting, CarrierName = "GreetingExtensions" };
        var method = new Method("goto", Integer, [new Parameter("lock", Integer)], Public) { DeclaringBlock = block };
        var access = new MemberAccessExpression(HandBuiltModel.Node(), Read(new LocalVariable("g", greeting)), "goto", Integer);

        Assert.Equal("HelloMist.@unsafe.GreetingExtensions.@goto(g, 70)", EmitExpression(new InvocationExpression(
            HandBuiltModel.Node(), access, [HandBuiltModel.Literal(Integer, 70L)], Integer, method, [Integer])));
    }

    private static Method Method(string name, ClassType returnType, List<Parameter> parameters, params Statement[] statements)
    {
        return new Method(name, returnType, parameters, PublicStatic) { Body = HandBuiltModel.Block(statements) };
    }

    private static LocalReferenceExpression Read(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    private static string EmitMethod(Method method)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        MethodEmitter.Emit(method, false, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string EmitExpression(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private string EmitType(MemberType type)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;
        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model.Model, diagnostics), model.Model.VariadicUses,
            writer, diagnostics, ref anyEmitted);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}