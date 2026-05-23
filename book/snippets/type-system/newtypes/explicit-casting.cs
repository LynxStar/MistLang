using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int raw = 99;
        UserId uid = ((UserId)raw);
        int back = ((int)uid);
        Console.WriteLine($"raw={raw}");
        Console.WriteLine($"uid={uid.Value}");
        Console.WriteLine($"back={back}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
