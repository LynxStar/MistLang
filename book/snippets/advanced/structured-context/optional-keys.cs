using Standard;

namespace Snippet;

public class Program
{
    public static string Required()
    {
        var s = (string)MistContext.Peek<string>();
        return s;
    }

    public static string? Optional()
    {
        var s = (string?)MistContext.Peek<MistContext.Optional<string>>();
        return s;
    }

    public static void Main()
    {
        {
            var __e1309 = "outer";
            MistContext.Push<string>(__e1309);
            try
            {
                string? absent = null;
                {
                    var __e1310 = absent;
                    MistContext.Push<MistContext.Optional<string>>(__e1310);
                    try
                    {
                        System.Console.WriteLine(Required());
                        System.Console.WriteLine(Optional() ?? "absent");
                        string? present = "inner";
                        {
                            var __e1311 = present;
                            MistContext.Push<MistContext.Optional<string>>(__e1311);
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
