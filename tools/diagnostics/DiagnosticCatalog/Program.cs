using System.Text;
using Mist.Common.Diagnostics;

namespace DiagnosticCatalog;

internal static class Program
{
    private static int Main(string[] args)
    {
        if(args.Length != 1)
        {
            Console.Error.WriteLine("Usage: DiagnosticCatalog <output path>");
            return 1;
        }

        var index = DiagnosticRegistry.Index;
        var problems = index.Validate();
        if(!problems.IsEmpty)
        {
            Console.Error.WriteLine(string.Join("\n", problems));
            return 1;
        }

        var path = Path.GetFullPath(args[0]);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Mist.Common.Diagnostics.DiagnosticCatalog.Render(index), new UTF8Encoding(false));
        return 0;
    }
}