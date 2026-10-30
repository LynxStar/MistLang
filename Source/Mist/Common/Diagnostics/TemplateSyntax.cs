using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// The one reading of a message template, shared by the registry validator and the renderer so
/// that what the validator accepts is exactly what the renderer can print. Placeholders are
/// <c>{name}</c> or, until Phase 3 of the migration, a positional <c>{0}</c>; <c>{{</c> and <c>}}</c>
/// are literal braces; <c>{name?text}</c> prints its text, placeholders included, only when the
/// Optional slot it names carries a value.
/// </summary>
internal static class TemplateSyntax
{
    internal abstract record Segment;

    internal sealed record Literal(string Text) : Segment;

    internal sealed record Placeholder(string Name) : Segment;

    internal sealed record Conditional(string Name, ImmutableArray<Segment> Body) : Segment;

    public static bool TryParse(string template, out ImmutableArray<Segment> segments, out string? error)
    {
        var position = 0;
        var parsed = ParseSegments(template, ref position, insideConditional: false, out error);
        if(parsed is null)
        {
            segments = [];
            return false;
        }

        if(position < template.Length)
        {
            segments = [];
            error = $"unexpected '}}' at offset {position}";
            return false;
        }

        segments = parsed.Value;
        return true;
    }

    public static bool IsPositional(ImmutableArray<Segment> segments)
    {
        var placeholders = Placeholders(segments).ToList();
        return placeholders.Count > 0 && placeholders.All(name => name.All(char.IsAsciiDigit));
    }

    public static IEnumerable<string> Placeholders(ImmutableArray<Segment> segments)
    {
        foreach(var segment in segments)
        {
            switch(segment)
            {
                case Placeholder placeholder:
                    yield return placeholder.Name;
                    break;
                case Conditional conditional:
                    yield return conditional.Name;
                    foreach(var inner in Placeholders(conditional.Body))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    public static IEnumerable<string> ConditionalNames(ImmutableArray<Segment> segments)
    {
        return segments.OfType<Conditional>().Select(conditional => conditional.Name);
    }

    private static ImmutableArray<Segment>? ParseSegments(string template, ref int position, bool insideConditional, out string? error)
    {
        var segments = ImmutableArray.CreateBuilder<Segment>();
        var literal = new System.Text.StringBuilder();
        error = null;

        while(position < template.Length)
        {
            var current = template[position];
            if(current == '}')
            {
                if(position + 1 < template.Length && template[position + 1] == '}')
                {
                    _ = literal.Append('}');
                    position += 2;
                    continue;
                }

                if(insideConditional)
                {
                    break;
                }

                error = $"unescaped '}}' at offset {position}";
                return null;
            }

            if(current != '{')
            {
                _ = literal.Append(current);
                position++;
                continue;
            }

            if(position + 1 < template.Length && template[position + 1] == '{')
            {
                _ = literal.Append('{');
                position += 2;
                continue;
            }

            var close = template.IndexOfAny(['}', '?'], position + 1);
            if(close < 0)
            {
                error = $"unclosed '{{' at offset {position}";
                return null;
            }

            var name = template[(position + 1)..close];
            if(!IsName(name))
            {
                error = $"placeholder '{name}' at offset {position} is not a slot name or a positional index";
                return null;
            }

            FlushLiteral(literal, segments);
            if(template[close] == '}')
            {
                segments.Add(new Placeholder(name));
                position = close + 1;
                continue;
            }

            if(insideConditional)
            {
                error = $"conditional '{name}' at offset {position} is nested inside another conditional";
                return null;
            }

            position = close + 1;
            var body = ParseSegments(template, ref position, insideConditional: true, out error);
            if(body is null)
            {
                return null;
            }

            if(position >= template.Length || template[position] != '}')
            {
                error = $"unclosed conditional '{name}'";
                return null;
            }

            position++;
            segments.Add(new Conditional(name, body.Value));
        }

        FlushLiteral(literal, segments);
        return segments.ToImmutable();
    }

    private static void FlushLiteral(System.Text.StringBuilder literal, ImmutableArray<Segment>.Builder segments)
    {
        if(literal.Length > 0)
        {
            segments.Add(new Literal(literal.ToString()));
            _ = literal.Clear();
        }
    }

    private static bool IsName(string name)
    {
        if(name.Length == 0)
        {
            return false;
        }

        if(name.All(char.IsAsciiDigit))
        {
            return true;
        }

        return (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}