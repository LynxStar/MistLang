using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class DefaultabilityRefusalTests
{
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