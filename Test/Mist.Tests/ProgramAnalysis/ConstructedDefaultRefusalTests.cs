using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ConstructedDefaultRefusalTests
{
    private readonly DeclarationFragments fragment = new();

    private readonly GenericDefaultShapes shapes;

    public ConstructedDefaultRefusalTests()
    {
        shapes = new GenericDefaultShapes(fragment.Types);
    }

    [Theory]
    [InlineData("List", "List<int>")]
    [InlineData("Array", "int[]")]
    public void DefaultOfAnIntrinsicConstruction_AdvisesExplicitConstruction(string name, string displayed)
    {
        var definition = new ClassType(name == "Array" ? "Array<,>" : "List<>", "Standard", null, null, null)
        {
            Modifiers = DeclarationFragments.Public with { IsIntrinsic = true },
        };
        _ = Parameter(definition);
        IMistType[] arguments = name == "Array"
            ? [shapes.Integer, new UnknownValueType(shapes.Integer)]
            : [shapes.Integer];
        var closed = Construct(definition, arguments);
        fragment.Root = Local(closed, new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), closed));

        var finding = Assert.Single(fragment.Run(new DefaultabilityAnalyzer()));

        Assert.Equal("MIST_PA_NOT_DEFAULTABLE", finding.Id.Value);
        Assert.Equal(31, finding.Location.Start.Column);
        Assert.Equal($"Type '{displayed}' has no default. Construct it explicitly, or use a type that has a default.", finding.Message);
    }

    [Theory]
    [InlineData("class", false)]
    [InlineData("class", true)]
    [InlineData("method", false)]
    [InlineData("method", true)]
    [InlineData("constructor-constrained-method", false)]
    public void DefaultOfATypeParameter_RequiresItsDefaultConstraint(string owner, bool constrained)
    {
        var maker = shapes.Type("Maker");
        var parameter = owner == "class"
            ? Parameter(maker, defaultConstrained: constrained)
            : DeclarationFragments.Parameter(DeclarationFragments.Method(maker, "Make", shapes.Integer), maker.Scope, "T");

        if(constrained && owner != "class")
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        }
        else if(owner == "constructor-constrained-method")
        {
            parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        }

        fragment.Root = HandBuiltModel.Block(
            new ReturnStatement(new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), parameter), parameter),
            new ReturnStatement(new DefaultExpression(DeclarationFragments.At<DefaultLiteralExpressionPrimaryNode>(50), parameter), parameter));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(constrained ? [] : [31, 51], findings.Select(finding => finding.Location.Start.Column));
        Assert.All(findings, finding =>
        {
            Assert.Equal("MIST_PA_NOT_DEFAULTABLE", finding.Id.Value);
            Assert.Contains("'T'", finding.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultOfAConstruction_IsRefusedAtTheUseOnlyWhereItsArgumentLeavesItNone(bool refused)
    {
        var box = Construct(shapes.Box(), refused ? shapes.Connection() : shapes.Integer);
        fragment.Root = Local(box, new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), box));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(refused ? ["MIST_PA_NOT_DEFAULTABLE"] : [], findings.Select(finding => finding.Id.Value));
        Assert.All(findings, finding => Assert.Contains("'Box<Connection>'", finding.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("constructorless", "connection", true)]
    [InlineData("constructorless", "intrinsic", false)]
    [InlineData("constructor-and-a-block", "connection", false)]
    public void NewOfAConstruction_IsRefusedOnlyWhereItIsTheDefaultAndItsArgumentLeavesItNone(string shape, string argument, bool refused)
    {
        var definition = shapes.Box(shape);
        var box = Construct(definition, argument == "connection" ? shapes.Connection() : shapes.Integer);
        var constructor = definition.ConstructorsList.FirstOrDefault();
        fragment.Root = Local(box, new NewExpression(HandBuiltModel.Node(30), box, [], constructor, [], []));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(refused ? [31] : [], findings.Select(finding => finding.Location.Start.Column));
        Assert.All(findings, finding => Assert.Equal("MIST_PA_NOT_DEFAULTABLE", finding.Id.Value));
    }

    [Theory]
    [InlineData("intrinsic", null)]
    [InlineData("connection", "MIST_PA_FIELD_HAS_NO_DEFAULT")]
    [InlineData("holder-parameter", null)]
    public void SlotOfAConstruction_IsRefusedAtTheHolderOnlyWhereItsArgumentLeavesItNone(string argument, string? refusal)
    {
        var box = shapes.Box();
        var holder = shapes.Type("Holder");
        IMistType filled = argument switch
        {
            "intrinsic" => shapes.Integer,
            "connection" => shapes.Connection(),
            _ => Parameter(holder, "U"),
        };
        _ = Slot(holder, "Held", Construct(box, filled));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(refusal is null ? [] : [refusal], findings.Select(finding => finding.Id.Value));
        Assert.All(findings, finding => Assert.Contains("Field 'Held' of 'Holder'", finding.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("constructor-and-a-block", "intrinsic", null)]
    [InlineData("constructor-and-a-block", "connection", "Box<Connection>")]
    [InlineData("constructor", "intrinsic", "Box<int>")]
    public void TypeOverAConstructedBase_IsRefusedAtItsDeclarationWhereThatBaseHasNoDefault(string shape, string argument, string? named)
    {
        var baseType = Construct(shapes.Box(shape), argument == "connection" ? shapes.Connection() : shapes.Integer);
        _ = shapes.Type("Sub", baseType: baseType);

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(named is null ? [] : ["MIST_PA_BASE_NOT_DEFAULTABLE"], findings.Select(finding => finding.Id.Value));
        Assert.All(findings, finding => Assert.Equal(
            $"Type 'Sub' needs a default because it declares no constructor, and its base '{named}' has none to start from. Use a base type that has a default, or give 'Sub' a constructor and no default block.",
            finding.Message));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("new")]
    public void ConstructionOfADefinitionRefusedForEveryArgument_IsLeftToThatRefusal(string use)
    {
        var box = shapes.Box();
        _ = Slot(box, "Link", shapes.Connection());
        var holder = shapes.Type("Holder");
        _ = Slot(holder, "Held", Construct(box, shapes.Integer));
        var sub = shapes.Type("Sub", baseType: Construct(box, shapes.Integer));
        var closed = Construct(box, shapes.Integer);
        fragment.Root = Local(sub, use == "default"
            ? new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), closed)
            : new NewExpression(HandBuiltModel.Node(30), closed, [], null, [], []));

        var finding = Assert.Single(fragment.Run(new DefaultabilityAnalyzer()));

        Assert.Contains("Field 'Link' of 'Box'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("intrinsic", 0)]
    [InlineData("connection", 1)]
    public void DefaultConstraintClosedOverAConstruction_JudgesItAsItsDefaultIsJudged(string argument, int expected)
    {
        var box = Construct(shapes.Box(), argument == "connection" ? shapes.Connection() : shapes.Integer);
        var maker = fragment.Class("Maker");
        var make = DeclarationFragments.Method(maker, "Make", shapes.Integer);
        var parameter = DeclarationFragments.Parameter(make, maker.Scope, "T");
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        fragment.Built.Model.ConstructorConstraintClosures.RecordMethod(
            make, [box], TypeSubstitution.Empty.With(parameter, box), HandBuiltModel.Node(55));

        var findings = fragment.Run(new ConstraintAnalyzer());

        Assert.Equal(expected, findings.Count);
        Assert.All(findings, finding => Assert.Contains("'default' constraint", finding.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorConstraintClosedOverAConstructorlessConstruction_RequiresItsSemanticDefault(bool refused)
    {
        var box = Construct(shapes.Box(), refused ? shapes.Connection() : shapes.Inner());
        var maker = fragment.Class("Maker");
        var make = DeclarationFragments.Method(maker, "Make", shapes.Integer);
        var parameter = DeclarationFragments.Parameter(make, maker.Scope, "T");
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        fragment.Built.Model.ConstructorConstraintClosures.RecordMethod(
            make, [box], TypeSubstitution.Empty.With(parameter, box), HandBuiltModel.Node(55));

        var findings = fragment.Run(new ConstraintAnalyzer());

        Assert.Equal(refused ? ["MIST_PA_CONSTRUCTOR_CONSTRAINT_NOT_SATISFIED"] : [], findings.Select(finding => finding.Id.Value));
        Assert.All(findings, finding => Assert.Equal(56, finding.Location.Start.Column));
    }

    [Theory]
    [InlineData("defaultable-storage", false)]
    [InlineData("nondefaultable-storage", true)]
    [InlineData("no-storage", false)]
    [InlineData("explicit-constructor", false)]
    public void ConstructedNewOverANativeBase_StillRequiresItsOwnImplicitStorageDefaults(string shape, bool refused)
    {
        var native = shapes.Type("Native");
        native.Assembly = MistAssembly.Ambient;
        _ = Constructor(native);
        var holder = shape == "no-storage" ? shapes.Type("Holder") : shapes.Box(name: "Holder");

        if(shape == "no-storage")
        {
            _ = Parameter(holder);
        }

        holder.BaseType = native;
        var constructor = shape == "explicit-constructor" ? Constructor(holder) : null;
        var closed = Construct(holder, shape == "defaultable-storage" ? shapes.Inner() : shapes.Connection());
        fragment.Root = Local(closed, new NewExpression(HandBuiltModel.Node(30), closed, [], constructor, [], []));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal(refused ? ["MIST_PA_NOT_DEFAULTABLE"] : [], findings.Select(finding => finding.Id.Value));
        Assert.All(findings, finding => Assert.Equal(31, finding.Location.Start.Column));
    }

    [Theory]
    [InlineData("fieldless-base-closed-by-a-value")]
    [InlineData("fieldless-base-closed-by-a-parameter")]
    [InlineData("class-base-reached-through-constructors")]
    [InlineData("struct-base-reached-through-constructors")]
    public void SampleShapeOverAConstructedBase_IsAdmitted(string shape)
    {
        if(shape.StartsWith("fieldless", StringComparison.Ordinal))
        {
            var lifted = shapes.Type("Lifted");
            _ = Parameter(lifted, "K");
            var narrow = shapes.Type("Narrow", baseType: Construct(lifted, shapes.Integer));
            var derived = shapes.Type("Derived");
            derived.BaseType = Construct(lifted, Parameter(derived, "K"));
            var opened = Construct(derived, shapes.Integer);
            fragment.Root = shape.EndsWith("value", StringComparison.Ordinal)
                ? Local(narrow, new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), narrow))
                : Local(opened, new NewExpression(HandBuiltModel.Node(30), opened, [], null, [], []));
        }
        else
        {
            var isStruct = shape.StartsWith("struct", StringComparison.Ordinal);
            var container = shapes.Type("Container", isStruct);
            var item = Parameter(container);
            _ = Slot(container, "Item", item);
            _ = Constructor(container, item);
            var derived = shapes.Type("DerivedContainer", isStruct);
            derived.BaseType = Construct(container, Parameter(derived));
            _ = Slot(derived, "Extra", shapes.Integer);
            _ = Constructor(derived, derived.TypeParametersList[0], shapes.Integer);
            var intBox = shapes.Type("IntBox", isStruct, Construct(container, shapes.Integer));
            _ = Constructor(intBox, shapes.Integer);
        }

        Assert.Empty(fragment.Run(new DefaultabilityAnalyzer()));
    }

    private static Block Local(IMistType type, Expression value)
    {
        return HandBuiltModel.Block(new LocalDeclarationStatement(new LocalVariable("made", type), value));
    }
}