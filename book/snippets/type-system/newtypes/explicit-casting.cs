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

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
