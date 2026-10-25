namespace Mist.Common.Diagnostics;

/// <summary>
/// What a condition is about, independent of the stage that noticed it and of its severity.
/// Exactly these thirteen tokens exist; catalog grouping and JSON filtering read nothing else.
/// </summary>
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