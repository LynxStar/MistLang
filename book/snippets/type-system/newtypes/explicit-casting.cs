using Standard;
using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        int raw = 99;
        UserId uid = ((UserId)raw);
        int back = ((int)uid);
        System.Console.WriteLine($"raw={raw}");
        System.Console.WriteLine($"uid={uid.Value}");
        System.Console.WriteLine($"back={back}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
