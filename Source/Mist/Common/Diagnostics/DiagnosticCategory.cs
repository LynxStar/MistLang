namespace Mist.Common.Diagnostics;

/// Condition categories are independent of reporting stage and severity; catalog and JSON consumers use this closed set.
public enum DiagnosticCategory
{
    Syn,
    Nam,
    Typ,
    Dec,
    Flw,
    Own,
    Eff,
    Interop,
    Bck,
    Cfg,
    Dep,
    Iox,
    Ice,
}

public static class DiagnosticCategoryExtensions
{
    public static string Token(this DiagnosticCategory category)
    {
        return category == DiagnosticCategory.Interop ? "INT" : category.ToString().ToUpperInvariant();
    }
}