namespace Mist.Driver.Generators;

internal interface IGeneratorCache
{
    CachedGeneration? Use(string kind, string key, TimeSpan timeout, Func<string, ProvenanceRecord?> produce);
}