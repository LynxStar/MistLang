using System;

namespace Snippet;

class Helpers
{
    public static string Describe<T>(T producer)
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
