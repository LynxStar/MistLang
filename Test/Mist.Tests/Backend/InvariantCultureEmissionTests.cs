// Tier 3 (Component): purity Requirement 13.2. Each text conversion, text order, case mapping and regular
// expression Mist performs is emitted naming the invariant culture, which a program run under one culture cannot
// tell from a program that reads the current one. Built by hand and handed to the body emitter; no parser, no
// Roslyn, no process.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
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

[Trait("Tier", "3")]
public sealed class InvariantCultureEmissionTests
{
    private const string Provider = "System.Globalization.CultureInfo.InvariantCulture";

    private const string RegexType = "System.Text.RegularExpressions.Regex";

    private static readonly MemberModifiers Instance = new() { Accessibility = Accessibility.Public, IsIntrinsic = true };

    private static readonly MemberModifiers Static = new() { Accessibility = Accessibility.Public, IsIntrinsic = true, IsStatic = true };

    private readonly HandBuiltModel model = new();

    private readonly ClassType text;

    private readonly ClassType real;

    private readonly ClassType integer;

    private readonly ClassType list;

    public InvariantCultureEmissionTests()
    {
        text = model.Type("String");
        real = model.Type("Double");
        integer = model.Type("Int32");
        list = new ClassType("List<>", "Standard", new Scope(null), null, null);
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
    }

    [Theory]
    [InlineData("case-mapping", "string up = \"i\".ToUpperInvariant();")]
    [InlineData("conversion-from-text", $"int parsed = System.Convert.ToInt32(\"1234\", {Provider});")]
    [InlineData("own-text", $"string shown = 1.5.ToString({Provider});")]
    [InlineData("concatenation", $"string glued = \"d=\" + 1.5.ToString({Provider});")]
    [InlineData("console", $"System.Console.WriteLine((-2.5).ToString({Provider}));")]
    [InlineData("negative-literal-to-console", $"System.Console.WriteLine((-9223372036854775808L).ToString({Provider}));")]
    [InlineData("numeric-hole", "string hole = System.FormattableString.Invariant($\"d={d}\");")]
    [InlineData("tuple-hole", "string tupled = $\"t={MistInvariant.Text(t)}\";")]
    [InlineData("unknown-to-console", "System.Console.WriteLine(MistInvariant.Text(u));")]
    public void ValueToText_NamesTheInvariantCulture(string form, string expected)
    {
        Statement statement = form switch
        {
            "case-mapping" => Declare("up", text, Call(Literal("i"), new Method("ToUpper", text, [], Instance) { DeclaringType = text })),
            "conversion-from-text" => Declare("parsed", integer, StaticCall(
                new Method("ToInt32", integer, [new Parameter("value", text)], Static) { DeclaringType = model.Type("Convert") }, Literal("1234"))),
            "own-text" => Declare("shown", text, Call(Literal(1.5), Universal())),
            "concatenation" => Declare("glued", text, Glued(Literal("d="), Literal(1.5))),
            "console" => WriteLine(new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Literal(2.5), real)),
            "negative-literal-to-console" => WriteLine(HandBuiltModel.Literal(model.Type("Int64"), long.MinValue)),
            "numeric-hole" => Declare("hole", text, Interpolated("d=", Local("d", real))),
            "tuple-hole" => Declare("tupled", text, Interpolated("t=", Local("t", model.Interner.GetOrCreateTupleType([real, integer])))),
            _ => WriteLine(Local("u", UnknownType.Instance)),
        };

        Assert.Contains(expected, Emit(statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text-hole", "string hole = $\"n={name}\";")]
    [InlineData("fixed-concatenation", "string glued = \"a\" + 'b' + true;")]
    [InlineData("boolean-to-console", "System.Console.WriteLine(true);")]
    public void TextNoCultureCanChange_IsLeftAsWritten(string form, string expected)
    {
        Statement statement = form switch
        {
            "text-hole" => Declare("hole", text, Interpolated("n=", Local("name", text))),
            "fixed-concatenation" => Declare("glued", text, Glued(Glued(Literal("a"), Literal('b')), Literal(true))),
            _ => WriteLine(Literal(true)),
        };

        Assert.Contains(expected, Emit(statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("String", "l.Sort(System.StringComparer.InvariantCulture);")]
    [InlineData("unknown", "l.Sort(MistInvariant.Comparer<object>());")]
    [InlineData(
        "(String, Int32)",
        "l.Sort(MistInvariant.Elements<(string, int)>(System.StringComparer.InvariantCulture, System.Collections.Generic.Comparer<int>.Default));")]
    [InlineData("Int32", "l.Sort();")]
    public void ListSort_OrdersTextByTheInvariantCulture(string element, string expected)
    {
        IMistType elementType = element switch
        {
            "unknown" => UnknownType.Instance,
            "(String, Int32)" => model.Interner.GetOrCreateTupleType([text, integer]),
            _ => model.Type(element),
        };
        var sort = new Method("Sort", model.Type("Void"), [], Instance) { DeclaringType = list };

        Assert.Contains(
            expected,
            Emit(new ExpressionStatement(Call(Local("l", model.Interner.GetOrCreateConstructedType(list, [elementType])), sort))),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("literal", $"new {RegexType}(\"(?i)i\", {RegexType}Options.Compiled | {RegexType}Options.CultureInvariant)")]
    [InlineData("construction", $"new {RegexType}(\"a\", {RegexType}Options.Compiled | {RegexType}Options.CultureInvariant)")]
    [InlineData("pattern", $"{RegexType}.IsMatch(__e")]
    public void ARegularExpression_IsCultureInvariant(string form, string expected)
    {
        var regex = model.Type("Regex");
        var boolean = model.Type("Boolean");
        var statement = form switch
        {
            "literal" => Declare("r", regex, new RegexLiteralExpression(HandBuiltModel.Node(), "(?i)i", regex)),
            "construction" => Declare("r", regex, new NewExpression(HandBuiltModel.Node(), regex, [Literal("a")], null, [], null)),
            _ => Declare("matched", boolean, new IsPatternExpression(
                HandBuiltModel.Node(), Literal("I"), new RegexPattern(HandBuiltModel.Node(), text, "(?i)i"), boolean)),
        };
        var emitted = Emit(statement);

        Assert.Contains(expected, emitted, StringComparison.Ordinal);
        Assert.Contains($"{RegexType}Options.CultureInvariant", emitted, StringComparison.Ordinal);
    }

    private static string Emit(Statement statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitStatement(statement, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private static LocalDeclarationStatement Declare(string name, IMistType type, Expression initializer)
    {
        return new LocalDeclarationStatement(new LocalVariable(name, type), initializer);
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static InvocationExpression Call(Expression receiver, Method method)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType);

        return new InvocationExpression(HandBuiltModel.Node(), access, [], method.ReturnType, method, null);
    }

    private static InvocationExpression StaticCall(Method method, params Expression[] arguments)
    {
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), method.DeclaringType!), method.Name, method.ReturnType);

        return new InvocationExpression(HandBuiltModel.Node(), access, arguments, method.ReturnType, method, null);
    }

    private LiteralExpression Literal(object value)
    {
        var type = value switch
        {
            string => text,
            double => real,
            char => model.Type("Char"),
            _ => model.Type("Boolean"),
        };

        return HandBuiltModel.Literal(type, value);
    }

    private BinaryExpression Glued(Expression left, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.Add, right, text);
    }

    private InterpolatedStringExpression Interpolated(string lead, Expression hole)
    {
        return new InterpolatedStringExpression(HandBuiltModel.Node(), [new InterpolatedStringText(lead), new InterpolatedStringHole(hole)], text);
    }

    private ExpressionStatement WriteLine(Expression argument)
    {
        var writeLine = new Method("WriteLine", model.Type("Void"), [new Parameter("value", UnknownType.Instance)], Static)
        {
            DeclaringType = model.Type("Console"),
        };

        return new ExpressionStatement(StaticCall(writeLine, argument));
    }

    private Method Universal()
    {
        return new Method("ToString", text, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringBlock = new ExtensionBlock("self", "Standard", new Scope(null), null),
        };
    }
}