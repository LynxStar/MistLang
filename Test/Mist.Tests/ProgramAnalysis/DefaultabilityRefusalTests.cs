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

    private static ConcreteType Counter(DeclarationFragments fragment, string shape, bool isStruct = false)
    {
        var integer = fragment.Built.Type("Int32");
        integer.Modifiers = DeclarationFragments.Public with { IsIntrinsic = true };
        ConcreteType counter = isStruct
            ? new StructType("Counter", "Refusal", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public,
            }
            : fragment.Class("Counter");

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
            counter.ConstructorsList.Add(new Constructor("Counter", counter, parameters, DeclarationFragments.Public)
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