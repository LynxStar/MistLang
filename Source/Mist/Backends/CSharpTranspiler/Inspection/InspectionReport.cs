namespace Mist.Backends.CSharpTranspiler.Inspection;

internal enum VisibleMemberKind
{
    Property,
    Method,
}

internal sealed record VisibleMember(
    VisibleMemberKind Kind,
    string Name,
    string Signature,
    bool IsProtected,
    string? DeclaringBase);

internal sealed record HiddenMember(string Name, string Reason, string? DeclaringBase);

internal sealed record InspectionReport(
    string TypeName,
    string Kind,
    string? Note,
    IReadOnlyList<VisibleMember> Visible,
    IReadOnlyList<HiddenMember> Hidden)
{
    public void Write(TextWriter output)
    {
        output.WriteLine($"{TypeName} ({Kind})");

        if(Note is not null)
        {
            output.WriteLine($"  {Note}");
        }

        if(Visible.Count == 0 && Hidden.Count == 0)
        {
            output.WriteLine("  no members");
            return;
        }

        if(Visible.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("  VISIBLE:");
            WriteSorted(output, Visible.Where(member => member.Kind == VisibleMemberKind.Property).Select(Line));
            WriteSorted(output, Visible.Where(member => member.Kind == VisibleMemberKind.Method).Select(Line));
        }

        if(Hidden.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("  HIDDEN:");
            WriteSorted(output, Hidden.Select(member => (member.Name, $"    {member.Name}: {member.Reason}{From(member.DeclaringBase)}")));
        }
    }

    private static (string Name, string Text) Line(VisibleMember member)
    {
        var declaration = member.Kind == VisibleMemberKind.Property
            ? $"    property {member.Name}: {member.Signature}"
            : $"    method {member.Name}{member.Signature}";
        var access = member.IsProtected ? " [protected]" : "";

        return (member.Name, declaration + access + From(member.DeclaringBase));
    }

    private static string From(string? declaringBase)
    {
        return declaringBase is null ? "" : $" [from {declaringBase}]";
    }

    private static void WriteSorted(TextWriter output, IEnumerable<(string Name, string Text)> lines)
    {
        var ordered = lines
            .OrderBy(line => line.Name, StringComparer.Ordinal)
            .ThenBy(line => line.Text, StringComparer.Ordinal);

        foreach(var (_, text) in ordered)
        {
            output.WriteLine(text);
        }
    }
}