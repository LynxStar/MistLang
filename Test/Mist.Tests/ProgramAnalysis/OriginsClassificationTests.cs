using Mist.ProgramAnalysis;
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

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// Where <see cref="Origins"/> says a value comes from when it is reached through a property pattern's field
/// bindings, or from inside a closure, as the purity design's Origins section defines it. A field hop is the
/// scope's own only through an owned field of an owning reference; a pure closure's captures, <c>this</c>
/// included, are External inputs (purity Requirement 16.4), a local closure's are its Own_Instance
/// (Requirement 5.4), and where a closure is called its captures answer the enclosing scope's origins.
/// </summary>
[Trait("Tier", "2")]
public sealed class OriginsClassificationTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType boolean;
    private readonly ClassType item = HandBuiltModel.Class("Item");
    private readonly ClassType box = HandBuiltModel.Class("Box");
    private readonly StructType pair = HandBuiltModel.Struct("Pair");
    private readonly ClassType widget = HandBuiltModel.Class("Widget");
    private readonly ClassType action = HandBuiltModel.Class("Action");
    private readonly Field level;

    public OriginsClassificationTests()
    {
        integer = model.Type("Int32");
        boolean = model.Type("Boolean");
        _ = Declare(box, "Kept", item, OwnershipKind.Owned);
        _ = Declare(box, "Shared", item, OwnershipKind.Unrestricted);
        _ = Declare(box, "Next", box, OwnershipKind.Owned);
        _ = Declare(box, "Link", box, OwnershipKind.Unrestricted);
        _ = Declare(pair, "Kept", item, OwnershipKind.Owned);
        level = Declare(widget, "Level", integer, OwnershipKind.Unrestricted);
    }

    [Theory]
    [InlineData("made", "Kept", "Own")]
    [InlineData("made", "Shared", "External")]
    [InlineData("given", "Kept", "External")]
    [InlineData("copy", "Kept", "External")]
    [InlineData("made", "Next.Kept", "Own")]
    [InlineData("made", "Link.Kept", "External")]
    [InlineData("made", "Next.Shared", "External")]
    public void AFieldBoundThroughAPropertyPatternIsOwnOnlyThroughOwnedFieldsOfAnOwningReference(string scrutinee, string path, string expected)
    {
        var made = new LocalVariable("made", box);
        var given = new LocalVariable("given", box) { Parameter = new Parameter("given", box) };
        var copy = new LocalVariable("copy", pair);
        var holder = scrutinee switch
        {
            "made" => made,
            "given" => given,
            _ => copy,
        };
        var bound = new LocalVariable("bound", item);
        var test = new IsPatternExpression(HandBuiltModel.Node(), Reference(holder), Hops(holder.Type, path.Split('.'), bound), boolean);
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(made, new NewExpression(HandBuiltModel.Node(), box, [], null, [], [])),
            new LocalDeclarationStatement(copy, new NewExpression(HandBuiltModel.Node(), pair, [], null, [], [])),
            new ExpressionStatement(test));

        Assert.Equal(Enum.Parse<Origin>(expected), new Origins(body, null, ScopeKind.Pure).Of(Reference(bound)));
    }

    [Theory]
    [InlineData("constructor", "Pure", "Own", "External")]
    [InlineData("frozen", "Pure", "Frozen", "External")]
    [InlineData("plain", "Local", "Own", "Own")]
    [InlineData("plain", "LocalReadonly", "Own", "Own")]
    public void APureClosureTakesThisAsAnInput_WhileALocalOneOwnsIt(string member, string scope, string outside, string inside)
    {
        var kind = Enum.Parse<ScopeKind>(scope);
        var lambda = Lambda(This());
        var enclosing = new Origins(HandBuiltModel.Block(Hold(lambda)), Member(member), kind);
        var closure = new Origins(lambda, lambda, kind, enclosing, capturesAreInputs: kind is ScopeKind.Pure);

        Assert.Equal(Enum.Parse<Origin>(outside), enclosing.Of(This()));
        Assert.Equal(Enum.Parse<Origin>(inside), closure.Of(This()));
    }

    [Theory]
    [InlineData("constructor", "this", "Own")]
    [InlineData("frozen", "this", "Frozen")]
    [InlineData("plain", "this", "External")]
    [InlineData("frozen", "function", "Frozen")]
    [InlineData("plain", "function", "External")]
    [InlineData("plain", "recursive", "External")]
    [InlineData("plain", "untouched", "Own")]
    public void AClosureReachingThisCapturesTheEnclosingScopesThis_ThroughTheLocalFunctionsItCalls(string member, string reach, string expected)
    {
        var touchBody = HandBuiltModel.Block();
        var touch = new Method("Touch", integer, [], default) { Body = touchBody };
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), touch), [], integer, touch, []);
        var read = new ExpressionStatement(new FieldReferenceExpression(HandBuiltModel.Node(), level));
        touchBody.Statements = reach switch
        {
            "recursive" => [new ExpressionStatement(call), read],
            "untouched" => [new ExpressionStatement(HandBuiltModel.Literal(integer, 0L))],
            _ => [read],
        };
        var lambda = Lambda(reach == "this" ? This() : call);
        var enclosing = new Origins(HandBuiltModel.Block(new LocalFunctionStatement(touch, []), Hold(lambda)), Member(member), ScopeKind.Pure);

        Assert.Equal(Enum.Parse<Origin>(expected), enclosing.OfCaptures(lambda));
    }

    private static Field Declare(MemberType holder, string name, IMistType type, OwnershipKind ownership)
    {
        var field = new Field(name, type, ownership, default) { DeclaringType = holder };
        ((ConcreteType)holder).FieldsList.Add(field);
        return field;
    }

    private static Pattern Hops(IMistType holder, IReadOnlyList<string> names, LocalVariable bound)
    {
        var field = ((ConcreteType)holder).FieldsList.Single(candidate => candidate.Name == names[0]);
        var inner = names.Count == 1 ? new CatchAllPattern(HandBuiltModel.Node(), field.Type, bound) : Hops(field.Type, names.Skip(1).ToList(), bound);

        return new PropertyPattern(HandBuiltModel.Node(), holder,
            [new PropertySubpattern(HandBuiltModel.Node(), field.Name, inner, field.Type, new FieldResult(field, holder, []))]);
    }

    private object Member(string member)
    {
        return member switch
        {
            "constructor" => new Constructor("Widget", widget, [], default) { DeclaringType = widget },
            "frozen" => new Method("Peek", integer, [], default) { DeclaringType = widget, DeclaredThisTier = ThisTier.Frozen },
            _ => new Method("Plain", integer, [], default) { DeclaringType = widget },
        };
    }

    private LambdaExpression Lambda(Expression result)
    {
        return new LambdaExpression(HandBuiltModel.Node(), [], result, null, false, action);
    }

    private LocalDeclarationStatement Hold(LambdaExpression lambda)
    {
        return new LocalDeclarationStatement(new LocalVariable("held", action), lambda);
    }

    private ThisExpression This()
    {
        return new ThisExpression(HandBuiltModel.Node(), widget);
    }

    private static LocalReferenceExpression Reference(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }
}