using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Mist.Common.Diagnostics;

internal static partial class RegistryValidator
{
    [GeneratedRegex(@"^MIST(?!0000)[0-9]{4}$")]
    private static partial Regex NumberShape();

    public static ImmutableArray<string> Validate(IEnumerable<RegistryEntry> entries, IEnumerable<Tombstone> tombstones)
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        var list = entries.ToList();
        var retired = tombstones.Select(tombstone => tombstone.Number.Value).ToHashSet(StringComparer.Ordinal);
        var known = list.Select(entry => entry.Descriptor.Number.Value).ToHashSet(StringComparer.Ordinal);

        foreach(var group in list.GroupBy(entry => entry.Descriptor.Number.Value ?? "", StringComparer.Ordinal))
        {
            if(group.Count() > 1)
            {
                problems.Add($"{group.Key}: declared by {string.Join(", ", group.Select(entry => entry.Field))}");
            }
        }

        foreach(var entry in list)
        {
            var descriptor = entry.Descriptor;
            var field = entry.Field;
            var number = descriptor.Number.Value;

            if(string.IsNullOrEmpty(number) || !NumberShape().IsMatch(number))
            {
                problems.Add($"{field}: number '{number}' is malformed");
            }
            else if(retired.Contains(number))
            {
                problems.Add($"{field}: number '{number}' is a tombstone");
            }

            ValidateTitle(field, descriptor.Title, problems);

            if(string.IsNullOrWhiteSpace(descriptor.Description))
            {
                problems.Add($"{field}: description is missing");
            }
            else if(descriptor.Description.Contains('\n'))
            {
                problems.Add($"{field}: description is not one paragraph");
            }
            else if(descriptor.Description.Trim() == descriptor.Template.Trim())
            {
                problems.Add($"{field}: description repeats the template");
            }

            if(!Enum.IsDefined(descriptor.Category))
            {
                problems.Add($"{field}: category '{descriptor.Category}' is not one of the thirteen tokens");
            }

            if(!Enum.IsDefined(descriptor.Policy))
            {
                problems.Add($"{field}: policy class '{descriptor.Policy}' is unknown");
            }

            if(descriptor.Policy != PolicyClass.Fixed && descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            {
                problems.Add($"{field}: a {descriptor.Policy} descriptor cannot default to Error");
            }

            if(descriptor.Category == DiagnosticCategory.Ice && descriptor.DefaultSeverity != DiagnosticSeverity.Error)
            {
                problems.Add($"{field}: an ICE descriptor must default to Error");
            }

            ValidateTemplate(field, descriptor, problems);

            foreach(var related in descriptor.Related)
            {
                if(!known.Contains(related.Value ?? ""))
                {
                    problems.Add($"{field}: related number '{related.Value}' is not in the registry");
                }
            }
        }

        return problems.ToImmutable();
    }

    private static void ValidateTitle(string field, string title, ImmutableArray<string>.Builder problems)
    {
        if(string.IsNullOrWhiteSpace(title))
        {
            problems.Add($"{field}: title is missing");
            return;
        }

        if(title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 6)
        {
            problems.Add($"{field}: title '{title}' has more than six words");
        }

        if(!char.IsUpper(title[0]))
        {
            problems.Add($"{field}: title '{title}' is not in sentence case");
        }

        if(".!?:;,".Contains(title[^1]))
        {
            problems.Add($"{field}: title '{title}' ends in punctuation");
        }
    }

    private static void ValidateTemplate(string field, MistDiagnostic descriptor, ImmutableArray<string>.Builder problems)
    {
        if(string.IsNullOrWhiteSpace(descriptor.Template))
        {
            problems.Add($"{field}: template is missing");
            return;
        }

        if(!TemplateSyntax.TryParse(descriptor.Template, out var segments, out var error))
        {
            problems.Add($"{field}: template does not parse: {error}");
            return;
        }

        var placeholders = TemplateSyntax.Placeholders(segments).ToHashSet(StringComparer.Ordinal);
        if(placeholders.Any(name => name.All(char.IsAsciiDigit)))
        {
            problems.Add($"{field}: positional placeholders are not allowed; declare named slots");
            return;
        }

        var slots = descriptor.Slots.ToDictionary(slot => slot.Name, StringComparer.Ordinal);
        foreach(var name in placeholders.Where(name => !slots.ContainsKey(name)))
        {
            problems.Add($"{field}: placeholder '{name}' is not a slot");
        }

        foreach(var slot in descriptor.Slots.Where(slot => !placeholders.Contains(slot.Name)))
        {
            problems.Add($"{field}: slot '{slot.Name}' has no placeholder");
        }

        foreach(var name in TemplateSyntax.ConditionalNames(segments))
        {
            if(slots.TryGetValue(name, out var slot) && slot.Kind != SlotKind.Optional)
            {
                problems.Add($"{field}: conditional segment '{name}' names a slot that is not Optional");
            }
        }
    }
}