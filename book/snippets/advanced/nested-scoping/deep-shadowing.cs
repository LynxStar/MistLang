using Standard;

namespace Snippet;

public class DeepShadowing
{
    public static void Main(string[] args)
    {
        string name = "first";
        {
            string name_1 = "second";
            {
                string name_2 = "third";
                System.Console.WriteLine(name_2);
            }
            System.Console.WriteLine(name_1);
        }
        System.Console.WriteLine(name);
    }

    public DeepShadowing()
    {
    }

    public static DeepShadowing __CreateDefault()
    {
        return new DeepShadowing();
    }
}
