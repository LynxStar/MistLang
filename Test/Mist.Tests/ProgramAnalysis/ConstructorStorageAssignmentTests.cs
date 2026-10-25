using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
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
public sealed class ConstructorStorageAssignmentTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("positive-field", true)]
    [InlineData("positive-field-initialized", false)]
    [InlineData("non-negative-field", false)]
    [InlineData("auto-property", true)]
    [InlineData("auto-getter-bodied-setter", true)]
    [InlineData("bodied-getter-alone", false)]
    [InlineData("static-auto-property", false)]
    public void ConstructorAssigningNothing_LeavesUnassignedOnlyStorageWithoutADefault(string slot, bool refused)
    {
        var integer = model.Type("Int32");
        integer.Modifiers = Public with { IsIntrinsic = true };
        var connection = HandBuiltModel.Class("Connection");
        connection.ConstructorsList.Add(new Constructor("Connection", connection, [new Parameter("port", integer)], Public));
        var owner = HandBuiltModel.Class("Account");

        if(slot.EndsWith("field", StringComparison.Ordinal) || slot.EndsWith("initialized", StringComparison.Ordinal))
        {
            owner.FieldsList.Add(new Field("Held", Refined(integer, slot.StartsWith("positive", StringComparison.Ordinal)), OwnershipKind.Unrestricted, Public)
            {
                DeclaringType = owner,
                Initializer = slot.EndsWith("initialized", StringComparison.Ordinal) ? HandBuiltModel.Literal(integer, 1L) : null,
            });
        }
        else
        {
            owner.PropertiesList.Add(Property(slot, connection, owner));
        }

        var body = HandBuiltModel.Block();
        var constructor = new Constructor("Account", owner, [], Public) { Body = body, DeclaringType = owner, DeclarationNode = HandBuiltModel.Node(37) };
        owner.ConstructorsList.Add(constructor);

        var findings = model.Analyze(new DefiniteAssignmentAnalyzer(), body, [owner], owner, constructor);

        if(!refused)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED", finding.Number.Value);
    }

    [Theory]
    [InlineData(false, "none", true)]
    [InlineData(false, "one-branch", true)]
    [InlineData(false, "every-branch", false)]
    [InlineData(false, "initializer", false)]
    [InlineData(false, "this-chain", false)]
    [InlineData(true, "none", true)]
    [InlineData(true, "one-branch", true)]
    [InlineData(true, "every-branch", false)]
    [InlineData(true, "initializer", false)]
    [InlineData(true, "this-chain", false)]
    public void DefaultBlockField_StillRequiresConstructorAssignmentOnEveryPath(bool isStruct, string assignment, bool refused)
    {
        ConcreteType owner = isStruct ? HandBuiltModel.Struct("Holder") : HandBuiltModel.Class("Holder");
        var need = NonDefaultable();
        var field = new Field("Item", need, OwnershipKind.Unrestricted, Public) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        owner.DefaultBlock = new DefaultBlock([new DefaultValueEntry(field.Name, HandBuiltModel.Literal(need))], new DefaultBlockDeclarationNode());

        if(assignment == "initializer")
        {
            field.Initializer = HandBuiltModel.Literal(need);
        }

        var target = new MemberAccessExpression(HandBuiltModel.Node(20), new ThisExpression(HandBuiltModel.Node(), owner),
            field.Name, need, member: new FieldResult(field, owner, []));

        AssertAssignment(owner, target, assignment, refused);
    }

    [Theory]
    [InlineData("auto-property", "none", true)]
    [InlineData("auto-property", "one-branch", true)]
    [InlineData("auto-property", "every-branch", false)]
    [InlineData("auto-property", "initializer", false)]
    [InlineData("auto-property", "this-chain", false)]
    [InlineData("get-only-auto-property", "none", true)]
    [InlineData("get-only-auto-property", "one-branch", true)]
    [InlineData("get-only-auto-property", "every-branch", false)]
    [InlineData("get-only-auto-property", "initializer", false)]
    [InlineData("get-only-auto-property", "this-chain", false)]
    [InlineData("backing-property", "none", true)]
    [InlineData("backing-property", "one-branch", true)]
    [InlineData("backing-property", "every-branch", false)]
    [InlineData("backing-property", "initializer", false)]
    [InlineData("backing-property", "this-chain", false)]
    public void NonDefaultablePropertyStorage_RequiresConstructorAssignmentOnEveryPath(string shape, string assignment, bool refused)
    {
        var owner = HandBuiltModel.Class("Holder");
        var property = Property(shape, NonDefaultable(), owner);
        owner.PropertiesList.Add(property);

        if(assignment == "initializer")
        {
            property.Initializer = HandBuiltModel.Literal(property.Type);
        }

        var target = new MemberAccessExpression(HandBuiltModel.Node(20), new ThisExpression(HandBuiltModel.Node(), owner),
            property.Name, property.Type, member: new PropertyResult(property, owner, []));

        AssertAssignment(owner, target, assignment, refused);
    }

    [Fact]
    public void DefaultBlockEntry_DoesNotAllowReadingTheFieldBeforeConstructorAssignment()
    {
        var owner = HandBuiltModel.Class("Holder");
        var need = NonDefaultable();
        var field = new Field("Item", need, OwnershipKind.Unrestricted, Public) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        owner.DefaultBlock = new DefaultBlock([new DefaultValueEntry(field.Name, HandBuiltModel.Literal(need))], new DefaultBlockDeclarationNode());
        var target = new MemberAccessExpression(HandBuiltModel.Node(20), new ThisExpression(HandBuiltModel.Node(), owner),
            field.Name, need, member: new FieldResult(field, owner, []));
        var body = HandBuiltModel.Block(new ExpressionStatement(target), Write(target));
        var constructor = Declare(owner, body);

        var finding = Assert.Single(model.Analyze(new DefiniteAssignmentAnalyzer(), body, [owner], owner, constructor));

        Assert.Equal("MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT", finding.Number.Value);
    }

    [Fact]
    public void StaticConstructor_DoesNotOweInstanceStorageAssignment()
    {
        var owner = HandBuiltModel.Class("Holder");
        owner.FieldsList.Add(new Field("Item", NonDefaultable(), OwnershipKind.Unrestricted, Public) { DeclaringType = owner });
        var body = HandBuiltModel.Block();
        var constructor = new Constructor(owner.Name, owner, [], Public with { IsStatic = true })
        {
            Body = body,
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(37),
        };
        owner.ConstructorsList.Add(constructor);

        Assert.Empty(model.Analyze(new DefiniteAssignmentAnalyzer(), body, [owner], owner, constructor));
    }

    private ClassType NonDefaultable()
    {
        var need = HandBuiltModel.Class("Need");
        need.ConstructorsList.Add(new Constructor(need.Name, need, [new Parameter("value", model.Type("Int32"))], Public));
        return need;
    }

    private void AssertAssignment(ConcreteType owner, Expression target, string assignment, bool refused)
    {
        var condition = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("condition", model.Type("Boolean")));
        var body = assignment switch
        {
            "one-branch" => HandBuiltModel.Block(new IfStatement(condition, HandBuiltModel.Block(Write(target)), null)),
            "every-branch" => HandBuiltModel.Block(new IfStatement(condition, HandBuiltModel.Block(Write(target)), HandBuiltModel.Block(Write(target)))),
            _ => HandBuiltModel.Block(),
        };
        Constructor constructor;

        if(assignment == "this-chain")
        {
            var assignedBody = HandBuiltModel.Block(Write(target));
            var assignedConstructor = Declare(owner, assignedBody);
            Assert.Empty(model.Analyze(new DefiniteAssignmentAnalyzer(), assignedBody, [owner], owner, assignedConstructor));
            constructor = new Constructor(owner.Name, owner, [new Parameter("ignored", model.Type("Int32"))], Public)
            {
                Body = body,
                DeclaringType = owner,
                DeclarationNode = HandBuiltModel.Node(37),
                ChainKind = ConstructorChainKind.This,
                ChainedConstructor = assignedConstructor,
            };
            owner.ConstructorsList.Add(constructor);
        }
        else
        {
            constructor = Declare(owner, body);
        }

        var findings = model.Analyze(new DefiniteAssignmentAnalyzer(), body, [owner], owner, constructor);

        Assert.Equal(refused ? ["MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED"] : [], findings.Select(finding => finding.Number.Value));
    }

    private static Constructor Declare(ConcreteType owner, Block body)
    {
        var constructor = new Constructor(owner.Name, owner, [], Public)
        {
            Body = body,
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(37),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static ExpressionStatement Write(Expression target)
    {
        return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), target, HandBuiltModel.Literal(target.Type), target.Type));
    }

    private static RefinedType Refined(IMistType integer, bool positive)
    {
        return new RefinedType(integer, HandBuiltModel.Node(), positive ? "PositiveInt" : "NonNegativeInt")
        {
            Predicate = new ComparisonPattern(HandBuiltModel.Node(), integer,
                positive ? ComparisonKind.GreaterThan : ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(integer, 0L)),
        };
    }

    private static Property Property(string slot, IMistType type, ConcreteType owner)
    {
        var auto = new PropertyAccessor { IsAuto = true };
        var bodied = new PropertyAccessor { Body = HandBuiltModel.Block() };

        return new Property("Link", type, slot == "static-auto-property" ? Public with { IsStatic = true } : Public)
        {
            DeclaringType = owner,
            Getter = slot.StartsWith("bodied", StringComparison.Ordinal) || slot == "backing-property" ? bodied : auto,
            Setter = slot switch
            {
                "bodied-getter-alone" or "get-only-auto-property" => null,
                "auto-getter-bodied-setter" or "backing-property" => bodied,
                _ => auto,
            },
            UsesBackingField = slot is "auto-getter-bodied-setter" or "backing-property",
        };
    }
}