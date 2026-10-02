using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
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
        Assert.Equal("MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED", finding.Id.Value);
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
            Getter = slot.StartsWith("bodied", StringComparison.Ordinal) ? bodied : auto,
            Setter = slot switch
            {
                "bodied-getter-alone" => null,
                "auto-getter-bodied-setter" => bodied,
                _ => auto,
            },
            UsesBackingField = slot == "auto-getter-bodied-setter",
        };
    }
}