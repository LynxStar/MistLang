using System.Collections.Immutable;
using System.Text;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record CatalogTypeText(string Name, ImmutableArray<string> Arguments)
{
    public static CatalogTypeText Parse(string text)
    {
        var name = new StringBuilder();
        var arguments = ImmutableArray.CreateBuilder<string>();
        var start = 0;
        var depth = 0;
        for(var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if(depth == 0)
            {
                if(current == '<')
                {
                    start = index + 1;
                    depth = 1;
                }
                else if(!char.IsWhiteSpace(current))
                {
                    _ = name.Append(current);
                }
            }
            else if(depth == 1 && current is ',' or '>')
            {
                arguments.Add(text[start..index].Trim());
                start = index + 1;
                if(current == '>')
                {
                    depth = 0;
                }
            }
            else if(current is '<' or '[' or '(')
            {
                depth++;
            }
            else if(current is '>' or ']' or ')')
            {
                depth--;
            }
        }

        return new CatalogTypeText(name.ToString(), arguments.ToImmutable());
    }

    public static ImmutableArray<ParameterFacts> Substitute(
        ImmutableArray<ParameterFacts> parameters, IEnumerable<string> names, ImmutableArray<string> arguments)
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var (name, argument) in names.Zip(arguments))
        {
            substitutions[name] = argument;
        }

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