namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal enum WithholdingReason
{
    NonPublic,
    AccessorOrOperator,
    Finalizer,
    UnresolvableConstraint,
    UnresolvableType,
}

internal readonly record struct Withholding(WithholdingReason Reason, Type? Offending = null);