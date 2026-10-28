using System.Text;

namespace Mist.Common.Diagnostics;

public static class DiagnosticCatalog
{
    public static string Render(RegistryIndex index)
    {
        var output = new StringBuilder("# Diagnostics\n\nGenerated from `DiagnosticRegistry.Index`. Regenerate with `tools/diagnostics/catalog.sh`; do not edit this page by hand.\n");
        var categories = index.Descriptors
            .OrderBy(descriptor => descriptor.Category)
            .ThenBy(descriptor => descriptor.Number.Value, StringComparer.Ordinal)
            .GroupBy(descriptor => descriptor.Category);

        foreach(var category in categories)
        {
            _ = output.Append("\n## ").Append(category.Key.Token()).Append("\n\n");
            _ = output.Append("| Number | Title | Description | Default severity | Policy class | Slots | Related numbers |\n");
            _ = output.Append("| --- | --- | --- | --- | --- | --- | --- |\n");
            foreach(var descriptor in category)
            {
                string[] cells =
                [
                    descriptor.Number.Value,
                    descriptor.Title,
                    descriptor.Description,
                    descriptor.DefaultSeverity.ToString(),
                    descriptor.Policy.ToString(),
                    descriptor.Slots.IsEmpty ? "None" : string.Join(", ", descriptor.Slots.Select(FormatSlot)),
                    descriptor.Related.IsEmpty ? "None" : string.Join(", ", descriptor.Related.Select(number => number.Value).Order(StringComparer.Ordinal)),
                ];
                _ = output.Append("| ").AppendJoin(" | ", cells.Select(Escape)).Append(" |\n");
            }
        }

        return output.ToString();
    }

    private static string FormatSlot(Slot slot)
    {
        var kind = slot.ElementKind is { } element ? $"{slot.Kind}<{element}>" : slot.Kind.ToString();

        return $"{slot.Name}: {kind}";
    }

    private static string Escape(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }
}