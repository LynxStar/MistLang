using Standard;

namespace Snippet;

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

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
