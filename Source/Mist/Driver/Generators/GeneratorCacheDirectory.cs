namespace Mist.Driver.Generators;

public static class GeneratorCacheDirectory
{
    public static string Root => GeneratorCache.Root;

    public static void Clean()
    {
        var generators = Path.Combine(Root, "generators");
        if(Directory.Exists(generators))
        {
            Directory.Delete(generators, recursive: true);
        }
    }
}