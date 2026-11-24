using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
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
public sealed class StructCopyFlowTests
{
    [Theory]
    [InlineData("binding")]
    [InlineData("parameter")]
    [InlineData("field")]
    public void MovingAnOwnedStructInvalidatesItsSource(string destination)
    {
        var (type, clones) = StructCopyDecisionTests.Mixed("mixed");
        var model = new HandBuiltModel();
        model.Model.Clones = clones;
        var source = new LocalVariable("m", type) { Ownership = OwnershipKind.Owned };
        var value = new LocalReferenceExpression(HandBuiltModel.Node(20), source);
        Statement move = destination switch
        {
            "parameter" => new ExpressionStatement(Call(type, OwnershipKind.Owned, value)),
            "field" => new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(),
                new FieldReferenceExpression(HandBuiltModel.Node(), new Field("Held", type, OwnershipKind.Owned, default)), value, type)),
            _ => new LocalDeclarationStatement(new LocalVariable("q", type) { Ownership = OwnershipKind.Owned }, value),
        };
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(source, new NewExpression(HandBuiltModel.Node(), type, [], null, [], [])), move,
            new ExpressionStatement(Call(type, OwnershipKind.Unrestricted, new LocalReferenceExpression(HandBuiltModel.Node(40), source))));

        var diagnostic = Assert.Single(model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0227", diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Position().Start.Column);
        Assert.Contains("m", diagnostic.Message);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("nested")]
    [InlineData("plain")]
    public void ASharedStructCopyLeavesTheSourceUsable(string shape)
    {
        var (type, clones) = StructCopyDecisionTests.Mixed(shape);
        var model = new HandBuiltModel();
        model.Model.Clones = clones;
        var local = new LocalVariable("m", type);
        var source = new LocalReferenceExpression(HandBuiltModel.Node(20), local);
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(local, new NewExpression(HandBuiltModel.Node(), type, [], null, [], [])),
            new LocalDeclarationStatement(new LocalVariable("q", type) { Ownership = OwnershipKind.Owned }, shape == "plain" ? source : new CloneExpression(source.Node, source, copiesOwnedFields: true)),
            new ExpressionStatement(Call(type, OwnershipKind.Unrestricted, new LocalReferenceExpression(HandBuiltModel.Node(40), local))));

        Assert.Empty(model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ACopyIntoANonOwnedBindingLeavesTheOwnedSourceUsable(bool assignment, bool throughField)
    {
        var (type, clones) = StructCopyDecisionTests.Mixed("mixed");
        var model = new HandBuiltModel();
        model.Model.Clones = clones;
        var holder = HandBuiltModel.Class("Holder");
        var field = new Field("Held", type, OwnershipKind.Unrestricted, default) { DeclaringType = holder };
        holder.FieldsList.Add(field);
        var source = new LocalVariable("m", throughField ? holder : type) { Ownership = OwnershipKind.Owned };
        Expression read(int offset)
        {
            var local = new LocalReferenceExpression(HandBuiltModel.Node(offset), source);
            return throughField
                ? new MemberAccessExpression(HandBuiltModel.Node(offset), local, field.Name, type, member: new FieldResult(field, holder, []))
                : local;
        }

        var destination = new LocalVariable("q", type);
        var value = read(0);
        Statement copy = assignment
            ? new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), destination), value, type))
            : new LocalDeclarationStatement(destination, value);
        var statements = new List<Statement>
        {
            new LocalDeclarationStatement(source, new NewExpression(HandBuiltModel.Node(), source.Type, [], null, [], [])),
        };
        if(assignment)
        {
            statements.Add(new LocalDeclarationStatement(destination, new NewExpression(HandBuiltModel.Node(), type, [], null, [], [])));
        }

        statements.Add(copy);
        statements.Add(new ExpressionStatement(Call(type, OwnershipKind.Unrestricted, read(40))));
        var body = HandBuiltModel.Block([.. statements]);

        Assert.Empty(model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Fact]
    public void AStructFieldCopyDoesNotLendItsClassHolder()
    {
        var (type, clones) = StructCopyDecisionTests.Mixed("mixed");
        var model = new HandBuiltModel();
        model.Model.Clones = clones;
        var holder = HandBuiltModel.Class("Holder");
        var field = new Field("Held", type, OwnershipKind.Unrestricted, default) { DeclaringType = holder };
        holder.FieldsList.Add(field);
        var source = new LocalVariable("h", holder) { Ownership = OwnershipKind.Owned };
        var copy = new LocalVariable("copy", type) { InfersOwnership = true };
        var value = new MemberAccessExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), source), field.Name, type,
            member: new FieldResult(field, holder, []));
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(source, new NewExpression(HandBuiltModel.Node(), holder, [], null, [], [])),
            new LocalDeclarationStatement(copy, value),
            new LocalDeclarationStatement(new LocalVariable("other", holder) { Ownership = OwnershipKind.Owned }, new LocalReferenceExpression(HandBuiltModel.Node(40), source)));

        Assert.Empty(model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASharedCopyRefusalNamesTheOwnedFieldTypeAndRemedy(bool mistType)
    {
        var (type, clones) = StructCopyDecisionTests.Mixed("mixed");
        var structure = Assert.IsType<StructType>(type);
        var held = new ClassType(mistType ? "Bad" : "StringBuilder", "Probe", new Scope(null), null,
            mistType ? DeclarationFragments.At<BodyTypeTypeDeclarationNode>() : null);
        if(mistType)
        {
            held.FieldsList.Add(new Field("Plain", structure.FieldsList[0].Type, OwnershipKind.Unrestricted, default) { DeclaringType = held });
        }

        structure.FieldsList[1] = new Field("Own", held, OwnershipKind.Owned, default) { DeclaringType = structure };
        var model = new HandBuiltModel();
        model.Model.Clones = clones;
        var local = new LocalVariable("m", type);
        var source = new LocalReferenceExpression(HandBuiltModel.Node(40), local);
        var copy = new CloneExpression(source.Node, source, copiesOwnedFields: true);
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(new LocalVariable("q", type) { Ownership = OwnershipKind.Owned }, copy));

        var diagnostic = Assert.Single(model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0506", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(1, diagnostic.Position().Start.Line);
        Assert.Equal(41, diagnostic.Position().Start.Column);
        Assert.Equal(mistType
            ? "Cannot clone this 'Mixed': 'Mixed' cannot be cloned: field 'Own' of 'Mixed' is owned but holds 'Bad', which cannot be cloned (field 'Plain' of 'Bad' is not owned and holds 'Style', which can change); make that field 'owned' or give the type a 'Clone()' of its own."
            : "Cannot clone this 'Mixed': 'Mixed' cannot be cloned: field 'Own' of 'Mixed' is owned but holds 'StringBuilder', which cannot be cloned (it implements no 'IClonable<T>'); give that field's type a 'Clone()' of its own or move from an owned source.", diagnostic.Message);
    }

    private static InvocationExpression Call(IMistType type, OwnershipKind ownership, Expression value)
    {
        var method = new Method("Take", new StructType("Void", "Standard", new Scope(null), null, null),
            [new Parameter("value", type, new ParameterModifier(false, false, ownership))], new MemberModifiers { IsStatic = true });
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [value], method.ReturnType, method, [type]);
    }
}