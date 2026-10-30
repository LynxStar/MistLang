using System.Reflection;
using Mist.Common.Diagnostics;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal enum WithholdingReason
{
    NonPublic,
    AccessorOrOperator,
    Finalizer,
    UnresolvableConstraint,
    UnresolvableType,
}

internal readonly record struct Withholding(WithholdingReason Reason, TypeValue? Offending = null)
{
    public Withholding(WithholdingReason reason, Type? offending)
        : this(reason, offending is null ? null : ClrTypeSnapshot.Of(offending))
    {
    }

    public WithholdingFrame Snapshot(MemberInfo member)
    {
        return new WithholdingFrame(new SymbolValue(MemberIdentity(member), member.Name), new EnumValue(Reason.ToString()), Offending);
    }

    public static string MemberIdentity(MemberInfo member)
    {
        var declaring = member.DeclaringType is { } type ? ClrTypeSnapshot.Of(type).Identity : "<global>";
        return $"{declaring}.{member.Name}#{member.Module.ModuleVersionId}:{member.MetadataToken}";
    }
}