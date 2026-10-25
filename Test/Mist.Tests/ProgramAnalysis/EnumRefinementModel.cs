using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

internal sealed class EnumRefinementModel
{
    public HandBuiltRefinements Facts { get; } = new();

    public EnumType SourceEnum(string name = "Color", IMistType? underlying = null)
    {
        return new EnumType(name, "Probe", new Scope(null), null, new EnumDeclarationNode())
        {
            UnderlyingType = underlying ?? Facts.Int,
        };
    }

    public static MemberAccessExpression Member(EnumType type, string name, Expression? value, int offset = 0)
    {
        var member = new EnumMember(name, false, value);
        type.MembersList.Add(member);
        return Access(type, member, offset);
    }

    public static MemberAccessExpression Access(EnumType type, EnumMember member, int offset = 0)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), new TypeReferenceExpression(HandBuiltModel.Node(), type),
            member.Name, type, member: new EnumMemberResult(member, type));
    }

    public LiteralExpression Number(EnumType type, object value)
    {
        return Facts.Literal(value, type.UnderlyingType);
    }

    public RefinedType AtLeast(EnumType type, Expression bound, string name = "Warm")
    {
        return Facts.Refine(type, ComparisonKind.GreaterThanOrEqual, bound, name);
    }

    public static string? Judge(Expression value, IMistType target)
    {
        var known = Requirement.Constant(value) is { } constant ? Admitted.Exactly(constant) : Admitted.Everything;
        return RefinementDecision.Judge(value, target, known)?.Number.Value;
    }
}