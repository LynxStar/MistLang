using System.Collections.Immutable;
using System.Text;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record CatalogTypeText(string Name, ImmutableArray<string> Arguments)
{
    public static CatalogTypeText Parse(string text)
    {
        var open = text.IndexOf('<');
        if(open < 0)
        {
            return new CatalogTypeText(text.Trim(), []);
        }

        var arguments = ImmutableArray.CreateBuilder<string>();
        var start = open + 1;
        var depth = 0;
        for(var index = start; index < text.Length; index++)
        {
            var current = text[index];
            if(depth == 0 && current is ',' or '>')
            {
                arguments.Add(text[start..index].Trim());
                start = index + 1;
                if(current == '>')
                {
                    break;
                }
            }

            if(current is '<' or '[' or '(')
            {
                depth++;
            }
            else if(current is '>' or ']' or ')')
            {
                depth--;
            }
        }

        return new CatalogTypeText(text[..open].Trim(), arguments.ToImmutable());
    }

    public static ImmutableArray<ParameterFacts> Substitute(
        ImmutableArray<ParameterFacts> parameters, IEnumerable<string> names, ImmutableArray<string> arguments)
    {
        var substitutions = names.Zip(arguments).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        return [.. parameters.Select(parameter => ParameterFacts.Declared(parameter.Name, Substitute(parameter.TypeText, substitutions)))];
    }

    private static string Substitute(string text, Dictionary<string, string> substitutions)
    {
        var result = new StringBuilder();
        for(var index = 0; index < text.Length;)
        {
            var start = index;
            if(char.IsLetter(text[index]) || text[index] == '_')
            {
                while(index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                {
                    index++;
                }

                var name = text[start..index];
                var previous = text[..start].TrimEnd();
                _ = result.Append(previous.EndsWith('.') ? name : substitutions.GetValueOrDefault(name, name));
            }
            else
            {
                _ = result.Append(text[index++]);
            }
        }

        return result.ToString();
    }
}