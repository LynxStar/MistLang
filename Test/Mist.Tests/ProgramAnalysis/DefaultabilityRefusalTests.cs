using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
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

[Trait("Tier", "2")]
public sealed class DefaultabilityRefusalTests
{
    [Fact]
    public void DefaultWhoseTypeAlreadyFailed_DoesNotCascade()
    {
        var fragment = new DeclarationFragments
        {
            Root = HandBuiltModel.Block(
                new ReturnStatement(new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), ErrorType.Instance), ErrorType.Instance),
                new ReturnStatement(new DefaultExpression(DeclarationFragments.At<DefaultLiteralExpressionPrimaryNode>(50), ErrorType.Instance), ErrorType.Instance)),
        };

        Assert.Empty(fragment.Run(new DefaultabilityAnalyzer()));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("constructed-new")]
    public void UnlocatedDeclarationFailure_DoesNotSilenceARefusalAtTheLocatedUse(string use)
    {
        var fragment = new DeclarationFragments();
        var holder = new ClassType("Holder", "Refusal", new Scope(null), null, null);
        holder.FieldsList.Add(new Field("Link", Counter(fragment, "argument-taking-constructor", name: "Connection"),
            OwnershipKind.Unrestricted, DeclarationFragments.Public));
        fragment.Types.Add(holder);
        _ = GenericDefaultShapes.Parameter(holder);
        var closed = GenericDefaultShapes.Construct(holder, fragment.Built.Type("Int32"));
        fragment.Root = use == "default" ? Defaults(holder) : DeclarationFragments.Returning(
            new NewExpression(HandBuiltModel.Node(30), closed, [], null, [], []));

        var findings = fragment.Run(new DefaultabilityAnalyzer());
        int[] expected = use == "default" ? [31, 51] : [31];

        Assert.Equal(expected, findings.Select(finding => finding.Location.Start.Column));
        Assert.All(findings, finding => Assert.Equal("MIST_PA_NOT_DEFAULTABLE", finding.Id.Value));
    }

    [Theory]
    [InlineData("parameterless-constructor", false)]
    [InlineData("parameterless-constructor", true)]
    [InlineData("argument-taking-constructor", false)]
    public void EachDefaultOfATypeWithAConstructorAndNoBlock_ReportsOnceAtItsUse(string shape, bool isStruct)
    {
        var fragment = new DeclarationFragments();
        fragment.Root = Defaults(Counter(fragment, shape, isStruct));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        Assert.Equal([31, 51], findings.Select(finding => finding.Location.Start.Column));
        Assert.All(findings, finding => Assert.Equal("MIST_PA_NOT_DEFAULTABLE", finding.Id.Value));
        Assert.All(findings, finding => Assert.Contains("'Counter'", finding.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("constructorless")]
    [InlineData("parameterless-constructor-and-a-block")]
    [InlineData("argument-taking-constructor-and-a-block")]
    public void DefaultsOfADefaultableType_AreAdmitted(string shape)
    {
        var fragment = new DeclarationFragments();
        fragment.Root = Defaults(Counter(fragment, shape));

        Assert.Empty(fragment.Run(new DefaultabilityAnalyzer()));
    }

    [Fact]
    public void DeclaringAndConstructingATypeWithAParameterlessConstructor_IsNotRefused()
    {
        var fragment = new DeclarationFragments();
        var counter = Counter(fragment, "parameterless-constructor");
        fragment.Root = HandBuiltModel.Block(new LocalDeclarationStatement(new LocalVariable("made", counter),
            new NewExpression(HandBuiltModel.Node(30), counter, [], counter.ConstructorsList[0], [], [])));

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
        Assert.Empty(fragment.Run(new DefaultabilityAnalyzer()));
    }

    [Theory]
    [InlineData("parameterless-constructor", 1)]
    [InlineData("parameterless-constructor-and-a-block", 0)]
    public void ClosingOverATypeWithAParameterlessConstructor_FailsOnlyItsDefaultConstraint(string shape, int expected)
    {
        var fragment = new DeclarationFragments();
        var counter = Counter(fragment, shape);
        var factory = fragment.Class("Factory");
        var create = DeclarationFragments.Method(factory, "Create", fragment.Built.Type("Int32"));
        var parameter = DeclarationFragments.Parameter(create, factory.Scope, "T");
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        fragment.Built.Model.ConstructorConstraintClosures.RecordMethod(
            create, [counter], TypeSubstitution.Empty.With(parameter, counter), HandBuiltModel.Node(55));

        var findings = fragment.Run(new ConstraintAnalyzer());

        Assert.Equal(expected, findings.Count);
        Assert.All(findings, finding =>
        {
            Assert.Equal("MIST_PA_CONSTRAINT_NOT_SATISFIED", finding.Id.Value);
            Assert.Contains("'default' constraint", finding.Message, StringComparison.Ordinal);
            Assert.Equal(56, finding.Location.Start.Column);
        });
    }

    [Theory]
    [InlineData("constructorless", "no constructor")]
    [InlineData("parameterless-constructor-and-a-block", "a default block")]
    [InlineData("parameterless-constructor", null)]
    public void FieldWithNoDefault_IsRefusedAtTheFieldOnlyWhereItsTypeNeedsADefault(string shape, string? declares)
    {
        var fragment = new DeclarationFragments();
        var holder = Counter(fragment, shape, name: "Holder");
        var connection = Counter(fragment, "argument-taking-constructor", name: "Connection");
        holder.FieldsList.Add(new Field("link", connection, OwnershipKind.Unrestricted, DeclarationFragments.Public)
        {
            DeclarationNode = HandBuiltModel.Node(40),
        });

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        if(declares is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_FIELD_HAS_NO_DEFAULT", finding.Id.Value);
        Assert.Equal(41, finding.Location.Start.Column);
        Assert.Contains($"Field 'link' of 'Holder' has no default, which 'Holder' needs because it declares {declares}: its type 'Connection'",
            finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("constructorless", "parameterless-constructor", "no constructor")]
    [InlineData("parameterless-constructor-and-a-block", "parameterless-constructor", "a default block")]
    [InlineData("constructorless", "argument-taking-constructor", "no constructor")]
    [InlineData("parameterless-constructor", "parameterless-constructor", null)]
    [InlineData("constructorless", "argument-taking-constructor-and-a-block", null)]
    [InlineData("constructorless", "ambient", null)]
    public void TypeNeedingADefault_IsRefusedAtItsDeclarationWhenItsBaseHasNone(string shape, string parent, string? declares)
    {
        var fragment = new DeclarationFragments();
        var baseType = Counter(fragment, parent == "ambient" ? "parameterless-constructor" : parent, name: "Base");
        var derived = Counter(fragment, shape, name: "Derived");
        derived.BaseType = baseType;

        if(parent == "ambient")
        {
            baseType.Assembly = MistAssembly.Ambient;
        }

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        if(declares is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_BASE_NOT_DEFAULTABLE", finding.Id.Value);
        Assert.Contains($"Type 'Derived' needs a default because it declares {declares}, and its base 'Base' has none",
            finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultOfATypeRefusedAtItsDeclaration_IsNotRefusedAgainWhereItIsTaken()
    {
        var fragment = new DeclarationFragments();
        var derived = Counter(fragment, "constructorless", name: "Derived");
        derived.BaseType = Counter(fragment, "parameterless-constructor", name: "Base");
        fragment.Root = Defaults(derived);

        var finding = Assert.Single(fragment.Run(new DefaultabilityAnalyzer()));

        Assert.Equal("MIST_PA_BASE_NOT_DEFAULTABLE", finding.Id.Value);
    }

    [Theory]
    [InlineData("constructorless", "no constructor")]
    [InlineData("parameterless-constructor-and-a-block", "a default block")]
    [InlineData("parameterless-constructor", null)]
    public void AutoPropertyWithNoDefault_IsRefusedAtThePropertyOnlyWhereItsTypeNeedsADefault(string shape, string? declares)
    {
        var fragment = new DeclarationFragments();
        var holder = Counter(fragment, shape, name: "Holder");
        holder.PropertiesList.Add(AutoProperty("Link", Counter(fragment, "argument-taking-constructor", name: "Connection")));

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        if(declares is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_PROPERTY_HAS_NO_DEFAULT", finding.Id.Value);
        Assert.Equal(41, finding.Location.Start.Column);
        Assert.Contains($"Property 'Link' of 'Holder' has no default, which 'Holder' needs because it declares {declares}: its type 'Connection'",
            finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("defaultable")]
    [InlineData("initialized")]
    [InlineData("static")]
    public void AutoPropertyThatIsAnsweredOrStatic_IsNotRefused(string shape)
    {
        var fragment = new DeclarationFragments();
        var holder = Counter(fragment, "constructorless", name: "Holder");
        var connection = Counter(fragment, "argument-taking-constructor", name: "Connection");
        var property = AutoProperty("Link", shape == "defaultable" ? Counter(fragment, "constructorless", name: "Meter") : connection,
            isStatic: shape == "static");
        property.Initializer = shape == "initialized" ? new NewExpression(HandBuiltModel.Node(), connection, [], null, [], []) : null;
        holder.PropertiesList.Add(property);

        Assert.Empty(fragment.Run(new DefaultabilityAnalyzer()));
    }

    [Theory]
    [InlineData("non-negative-field", "constructorless", null)]
    [InlineData("positive-field", "constructorless", "no constructor")]
    [InlineData("positive-property", "constructorless", "no constructor")]
    [InlineData("positive-field", "parameterless-constructor-and-a-block", "a default block")]
    [InlineData("positive-field-initialized", "constructorless", null)]
    [InlineData("positive-field-with-an-entry", "parameterless-constructor-and-a-block", null)]
    [InlineData("positive-field", "parameterless-constructor", null)]
    public void RefinementSlot_IsRefusedWhereItsPredicateRefusesTheBasesZero(string slot, string shape, string? declares)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var holder = Counter(fragment, shape, name: "Factory");
        var refined = new RefinedType(integer, HandBuiltModel.Node(), slot.StartsWith("positive", StringComparison.Ordinal) ? "PositiveInt" : "NonNegativeInt")
        {
            Predicate = new ComparisonPattern(HandBuiltModel.Node(), integer,
                slot.StartsWith("positive", StringComparison.Ordinal) ? ComparisonKind.GreaterThan : ComparisonKind.GreaterThanOrEqual,
                HandBuiltModel.Literal(integer, 0L)),
        };

        if(slot == "positive-property")
        {
            holder.PropertiesList.Add(AutoProperty("Held", refined));
        }
        else
        {
            holder.FieldsList.Add(new Field("Held", refined, OwnershipKind.Unrestricted, DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(40),
                Initializer = slot.EndsWith("initialized", StringComparison.Ordinal) ? HandBuiltModel.Literal(integer, 1L) : null,
            });
        }

        if(slot.EndsWith("with-an-entry", StringComparison.Ordinal))
        {
            holder.DefaultBlock = new DefaultBlock([new DefaultValueEntry("Held", HandBuiltModel.Literal(integer, 1L))],
                DeclarationFragments.At<DefaultBlockDeclarationNode>());
        }

        var findings = fragment.Run(new DefaultabilityAnalyzer());

        if(declares is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_REFINEMENT_ZERO_NOT_ADMITTED", finding.Id.Value);
        Assert.Equal(41, finding.Location.Start.Column);
        Assert.Contains($"'Held' of 'Factory' has no default, which 'Factory' needs because it declares {declares}: its type 'PositiveInt' would start from the zero of 'int'",
            finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChainOfTypesNeedingADefault_IsRefusedOnceWhereTheDefaultIsMissing()
    {
        var fragment = new DeclarationFragments();
        var connection = Counter(fragment, "argument-taking-constructor", name: "Connection");
        var inner = Holding(fragment, "Inner", connection, 60);
        var middle = Holding(fragment, "Middle", inner, 50);
        var outer = Holding(fragment, "Outer", middle, 40);
        fragment.Root = Defaults(outer);

        var finding = Assert.Single(fragment.Run(new DefaultabilityAnalyzer()));

        Assert.Equal("MIST_PA_FIELD_HAS_NO_DEFAULT", finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
    }

    [Fact]
    public void ChainOfBasesNeedingADefault_IsRefusedOnceWhereTheDefaultIsMissing()
    {
        var fragment = new DeclarationFragments();
        var root = Holding(fragment, "Root", Counter(fragment, "argument-taking-constructor", name: "Connection"), 60);
        var middle = Counter(fragment, "constructorless", name: "Middle");
        middle.BaseType = root;
        Counter(fragment, "constructorless", name: "Leaf").BaseType = middle;

        var finding = Assert.Single(fragment.Run(new DefaultabilityAnalyzer()));

        Assert.Equal("MIST_PA_FIELD_HAS_NO_DEFAULT", finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
    }

    [Fact]
    public void HolderOfATypeWithoutADefaultItRefusesNothingFor_IsRefusedItself()
    {
        var fragment = new DeclarationFragments();
        var clr = Counter(fragment, "parameterless-constructor", name: "Attribute");
        clr.Assembly = MistAssembly.Ambient;
        var converter = Counter(fragment, "constructorless", name: "Converter");
        converter.BaseType = clr;
        _ = Holding(fragment, "Holder", converter, 40);

        var finding = Assert.Single(fragment.Run(new DefaultabilityAnalyzer()));

        Assert.Equal("MIST_PA_FIELD_HAS_NO_DEFAULT", finding.Id.Value);
        Assert.Equal(41, finding.Location.Start.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CycleOfTypesNeedingADefault_IsRefusedAtEachFieldClosingIt(bool throughAnother)
    {
        var fragment = new DeclarationFragments();
        var node = fragment.Class("Node");
        var other = throughAnother ? fragment.Class("Other") : node;
        node.FieldsList.Add(FieldAt("next", other, 40));

        if(throughAnother)
        {
            other.FieldsList.Add(FieldAt("back", node, 50));
        }

        var findings = fragment.Run(new DefaultabilityAnalyzer());
        int[] expected = throughAnother ? [41, 51] : [41];

        Assert.Equal(expected, findings.Select(finding => finding.Location.Start.Column));
        Assert.All(findings, finding => Assert.Equal("MIST_PA_FIELD_HAS_NO_DEFAULT", finding.Id.Value));
    }

    private static ClassType Holding(DeclarationFragments fragment, string name, IMistType held, int offset)
    {
        var holder = fragment.Class(name);
        holder.FieldsList.Add(FieldAt(char.ToLowerInvariant(held.Name[0]) + held.Name[1..], held, offset));
        return holder;
    }

    private static Field FieldAt(string name, IMistType type, int offset)
    {
        return new Field(name, type, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclarationNode = HandBuiltModel.Node(offset) };
    }

    private static Property AutoProperty(string name, IMistType type, bool isStatic = false)
    {
        return new Property(name, type, DeclarationFragments.Public with { IsStatic = isStatic })
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclarationNode = HandBuiltModel.Node(40),
        };
    }

    private static ConcreteType Counter(DeclarationFragments fragment, string shape, bool isStruct = false, string name = "Counter")
    {
        var integer = fragment.Built.Type("Int32");
        integer.Modifiers = DeclarationFragments.Public with { IsIntrinsic = true };
        ConcreteType counter = isStruct
            ? new StructType(name, "Refusal", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public,
            }
            : fragment.Class(name);

        if(isStruct)
        {
            fragment.Types.Add(counter);
        }

        counter.FieldsList.Add(new Field("start", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public)
        {
            Initializer = HandBuiltModel.Literal(integer, 1L),
        });
        counter.FieldsList.Add(new Field("step", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public));

        if(shape != "constructorless")
        {
            List<Parameter> parameters = shape.StartsWith("argument-taking", StringComparison.Ordinal) ? [new Parameter("start", integer)] : [];
            counter.ConstructorsList.Add(new Constructor(name, counter, parameters, DeclarationFragments.Public)
            {
                DeclaringType = counter,
                DeclarationNode = HandBuiltModel.Node(20),
            });
        }

        if(shape.EndsWith("and-a-block", StringComparison.Ordinal))
        {
            counter.DefaultBlock = new DefaultBlock([new DefaultValueEntry("step", HandBuiltModel.Literal(integer, 2L, 25))],
                DeclarationFragments.At<DefaultBlockDeclarationNode>());
        }

        return counter;
    }

    private static Block Defaults(ConcreteType type)
    {
        return HandBuiltModel.Block(
            new LocalDeclarationStatement(new LocalVariable("named", type),
                new DefaultExpression(DeclarationFragments.At<DefaultExpressionPrimaryNode>(30), type)),
            new LocalDeclarationStatement(new LocalVariable("targeted", type),
                new DefaultExpression(DeclarationFragments.At<DefaultLiteralExpressionPrimaryNode>(50), type)));
    }
}