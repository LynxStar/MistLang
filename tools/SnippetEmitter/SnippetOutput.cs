namespace SnippetEmitter;

internal static class SnippetOutput
{
    internal static bool Write(SnippetResult result)
    {
        if(result.Emitted is null)
        {
            return false;
        }

        var path = Path.ChangeExtension(result.Path, ".cs");
        var emitted = Normalize(result.Emitted);
        if(File.ReadAllText(path).ReplaceLineEndings("\n") == emitted)
        {
            return false;
        }

        File.WriteAllText(path, emitted);
        return true;
    }

    internal static string Normalize(string emitted)
    {
        var normalized = emitted.ReplaceLineEndings("\n");
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}