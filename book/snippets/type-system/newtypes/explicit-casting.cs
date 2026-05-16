using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Cast from source type to newtype
        int raw = 99;
        UserId uid = ((UserId)raw);

        // Cast from newtype back to source type
        int back = ((int)uid);

        Console.WriteLine($"raw={raw}");
        Console.WriteLine($"uid={uid.Value}");
        Console.WriteLine($"back={back}");
    }
}
