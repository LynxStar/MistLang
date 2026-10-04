using Standard;

namespace Snippet;

public class Program
{
    public static string Required()
    {
        var s = (string)MistContext.Peek<string>();
        return s;
    }

    public static string Optional()
    {
        var s = (string)MistContext.Peek<MistContext.Optional<string>>();
        return s;
    }

    public static void Main()
    {
        {
            var __e1 = "outer";
            MistContext.Push<string>(__e1);
            try
            {
                string absent = null;
                {
                    var __e2 = absent;
                    MistContext.Push<MistContext.Optional<string>>(__e2);
                    try
                    {
                        System.Console.WriteLine(Required());
                        System.Console.WriteLine(Optional() ?? "absent");
                        string present = "inner";
                        {
                            var __e3 = present;
                            MistContext.Push<MistContext.Optional<string>>(__e3);
                            try
                            {
                                System.Console.WriteLine(Optional());
                            }
                            finally
                            {
                                MistContext.Pop<MistContext.Optional<string>>();
                            }
                        }
                        System.Console.WriteLine(Optional() ?? "absent");
                    }
                    finally
                    {
                        MistContext.Pop<MistContext.Optional<string>>();
                    }
                }
                System.Console.WriteLine(Required());
            }
            finally
            {
                MistContext.Pop<string>();
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
