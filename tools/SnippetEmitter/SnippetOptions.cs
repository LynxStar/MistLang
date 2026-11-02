namespace SnippetEmitter;

internal sealed record SnippetOptions(string[] Paths, int Jobs, bool Timings, bool List, bool Help)
{
    internal static SnippetOptions Parse(string[] args)
    {
        var paths = new List<string>();
        var jobs = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        var timings = false;
        var list = false;
        var help = false;
        var positional = false;
        for(var i = 0; i < args.Length; i++)
        {
            if(positional)
            {
                paths.Add(args[i]);
                continue;
            }

            switch(args[i])
            {
                case "--jobs":
                    if(++i >= args.Length || !int.TryParse(args[i], out jobs) || jobs < 1)
                    {
                        throw new ArgumentException("--jobs requires a positive integer.");
                    }

                    break;
                case "--timings":
                    timings = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                case "--":
                    positional = true;
                    break;
                default:
                    if(args[i].StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option: {args[i]}");
                    }

                    paths.Add(args[i]);
                    break;
            }
        }

        return new SnippetOptions(paths.Count == 0 ? ["book/snippets"] : [.. paths], jobs, timings, list, help);
    }
}