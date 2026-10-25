using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Resolution;
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

[Trait("Tier", "2")]
public sealed class RefStructPositionSiteTests
{
    private readonly HandBuiltModel model = new();
    private readonly StructType cursor = new("Cursor", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } };

    [Theory]
    [InlineData("Union", "MIST_PA_REF_STRUCT_UNION_CONSTITUENT")]
    [InlineData("Tuple", "MIST_PA_REF_STRUCT_ELEMENT")]
    [InlineData("Array", "MIST_PA_REF_STRUCT_ELEMENT")]
    [InlineData("TypeArgument", "MIST_PA_REF_STRUCT_TYPE_ARGUMENT")]
    public void AWrittenCompositeHoldingARefStructIsRefusedWhereItWasWritten(string kind, string expected)
    {
        model.Model.CompositeApplications.Record(Enum.Parse<CompositeKind>(kind), [cursor, model.Type("String")], HandBuiltModel.Node(37));

        AssertSite(model.Analyze(new RefStructPositionAnalyzer(), HandBuiltModel.Block()), expected);
    }

    [Fact]
    public void AWrittenOptionalOfARefStructIsRefused()
    {
        model.Model.OptionalApplications.Record(cursor, HandBuiltModel.Node(37));

        AssertSite(model.Analyze(new RefStructPositionAnalyzer(), HandBuiltModel.Block()), "MIST_PA_REF_STRUCT_OPTIONAL");
    }

    [Fact]
    public void CompositesOfHeapTypesAndCompositesOutsideTheSourceAreLeftAlone()
    {
        model.Model.CompositeApplications.Record(CompositeKind.Union, [model.Type("Int32"), model.Type("String")], HandBuiltModel.Node(37));
        model.Model.CompositeApplications.Record(CompositeKind.Tuple, [cursor, cursor], HandBuiltModel.Node(37, "ambient.mist"));
        model.Model.OptionalApplications.Record(model.Type("Int32"), HandBuiltModel.Node(37));

        Assert.Empty(model.Analyze(new RefStructPositionAnalyzer(), HandBuiltModel.Block()));
    }

    [Theory]
    [InlineData("tuple", "MIST_PA_REF_STRUCT_ELEMENT")]
    [InlineData("array", "MIST_PA_REF_STRUCT_ELEMENT")]
    public void AnInferredLiteralHoldingARefStructIsRefusedWhereItIsFormed(string shape, string expected)
    {
        AssertSite(model.Analyze(new RefStructPositionAnalyzer(), new ExpressionStatement(Literal(shape, isTargetTyped: false))), expected);
    }

    [Theory]
    [InlineData("tuple")]
    [InlineData("array")]
    public void ALiteralTypedByItsTargetLeavesTheRefusalToTheWrittenTarget(string shape)
    {
        Assert.Empty(model.Analyze(new RefStructPositionAnalyzer(), new ExpressionStatement(Literal(shape, isTargetTyped: true))));
    }

    [Theory]
    [InlineData("class-property", "MIST_PA_REF_STRUCT_PROPERTY")]
    [InlineData("ref-struct-static-field", "MIST_PA_REF_STRUCT_STATIC_STORAGE")]
    [InlineData("ref-struct-static-auto-property", "MIST_PA_REF_STRUCT_STATIC_STORAGE")]
    public void StorageOffTheStackIsRefusedAtItsDeclaration(string shape, string expected)
    {
        var fragment = new DeclarationFragments();
        MemberType container = shape.StartsWith("class", StringComparison.Ordinal) ? fragment.Class("Holder") : RefStruct(fragment);
        Declare(container, shape);

        AssertSite(fragment.Run(new RefStructPositionAnalyzer()), expected);
    }

    [Fact]
    public void ARefStructFieldOrComputedStaticPropertyOfARefStructIsAdmitted()
    {
        var fragment = new DeclarationFragments();
        var container = RefStruct(fragment);
        Declare(container, "instance-field");
        Declare(container, "static-computed-property");

        Assert.Empty(fragment.Run(new RefStructPositionAnalyzer()));
    }

    [Fact]
    public void ARefStructBoundToAnAbstractItImplementsIsRefusedAsBoxing()
    {
        var thing = new AbstractType("IThing", "Probe", new Scope(null), null, null);
        cursor.BaseAbstractTypes.Add(thing);
        var root = new LocalDeclarationStatement(new LocalVariable("t", thing), HandBuiltModel.Literal(cursor, offset: 37));

        AssertSite(model.Analyze(new AssignabilityAnalyzer(), root), "MIST_PA_REF_STRUCT_BOXED");
    }

    [Fact]
    public void ARefStructCastToAnAbstractItImplementsIsRefusedAsBoxing()
    {
        var thing = new AbstractType("IThing", "Probe", new Scope(null), null, null);
        cursor.BaseAbstractTypes.Add(thing);
        var root = new CastExpression(HandBuiltModel.Node(37), HandBuiltModel.Literal(cursor), thing, [], thing);

        AssertSite(model.Analyze(new CastAnalyzer(), root), "MIST_PA_REF_STRUCT_BOXED");
    }

    [Theory]
    [InlineData("is")]
    [InlineData("negated-is")]
    [InlineData("switch-arm")]
    public void ARefStructTypeTestedAgainstAnAbstractItImplementsIsRefusedAsBoxing(string shape)
    {
        var thing = new AbstractType("IThing", "Probe", new Scope(null), null, null);
        cursor.BaseAbstractTypes.Add(thing);
        var boolean = model.Type("Boolean");
        Pattern test = new TypeTestPattern(HandBuiltModel.Node(37), thing, new LocalVariable("t", thing));
        Expression root = shape switch
        {
            "is" => new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(cursor), test, boolean),
            "negated-is" => new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(cursor), new NegatedPattern(HandBuiltModel.Node(), cursor, test), boolean),
            _ => new SwitchExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(cursor), [new SwitchArm(HandBuiltModel.Node(), test, null, HandBuiltModel.Literal(boolean))], boolean),
        };

        AssertSite(model.Analyze(new RefStructPositionAnalyzer(), new ExpressionStatement(root)), "MIST_PA_REF_STRUCT_BOXED");
    }

    [Fact]
    public void ARefStructTypeTestedAgainstItsOwnTypeIsAdmitted()
    {
        var test = new TypeTestPattern(HandBuiltModel.Node(37), cursor, new LocalVariable("c", cursor));
        var root = new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(cursor), test, model.Type("Boolean"));

        Assert.Empty(model.Analyze(new RefStructPositionAnalyzer(), new ExpressionStatement(root)));
    }

    [Fact]
    public void ARefStructBoundToItsOwnTypeIsAdmitted()
    {
        var root = new LocalDeclarationStatement(new LocalVariable("c", cursor), HandBuiltModel.Literal(cursor, offset: 37));

        Assert.Empty(model.Analyze(new AssignabilityAnalyzer(), root));
    }

    private Expression Literal(string shape, bool isTargetTyped)
    {
        var integer = model.Type("Int32");
        return shape == "tuple"
            ? new TupleLiteralExpression(
                HandBuiltModel.Node(37), [HandBuiltModel.Literal(cursor), HandBuiltModel.Literal(integer)], [null, null],
                model.Interner.GetOrCreateTupleType([cursor, integer]))
            { IsTargetTyped = isTargetTyped }
            : new ArrayLiteralExpression(
                HandBuiltModel.Node(37), cursor, [HandBuiltModel.Literal(cursor)],
                model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [cursor]))
            { IsTargetTyped = isTargetTyped };
    }

    private static StructType RefStruct(DeclarationFragments fragment)
    {
        var type = new StructType("Lease", "Refusal", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = DeclarationFragments.Public with { IsRef = true },
        };
        fragment.Types.Add(type);
        return type;
    }

    private void Declare(MemberType container, string shape)
    {
        var isStatic = shape.Contains("static", StringComparison.Ordinal);
        var modifiers = DeclarationFragments.Public with { IsStatic = isStatic };

        if(shape.Contains("field", StringComparison.Ordinal))
        {
            ((ConcreteType)container).FieldsList.Add(new Field("Data", cursor, OwnershipKind.Unrestricted, modifiers)
            {
                DeclaringType = container,
                DeclarationNode = HandBuiltModel.Node(37),
            });
            return;
        }

        container.PropertiesList.Add(new Property("Data", cursor, modifiers)
        {
            DeclaringType = container,
            DeclarationNode = HandBuiltModel.Node(37),
            Getter = shape.Contains("computed", StringComparison.Ordinal) ? null : new PropertyAccessor { IsAuto = true },
        });
    }

    private static void AssertSite(IReadOnlyList<Diagnostic> diagnostics, string expected)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expected, diagnostic.Number.Value);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }
}