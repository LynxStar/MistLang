using System;

namespace Snippet;

public interface IStringable
{
    string Stringify();
}
public interface IProducer<TOutput>
    where TOutput : IStringable
{
    TOutput CreateOutput(string msg);
}
internal class Helpers
{
    public static string Describe<T, TOutput>(T producer)
        where T : IProducer<TOutput>
        where TOutput : IStringable
    {
        T.Output output = producer.CreateOutput("test");
        return output.Stringify();
    }

    public static Helpers __CreateDefault()
    {
        var inst = new Helpers();
        return inst;
    }
}
