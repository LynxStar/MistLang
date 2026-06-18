using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var input = "hello";
        var category = input switch
        {
            string __s when System.Text.RegularExpressions.Regex.IsMatch(__s, "^(?:^\\d+$)$") => "number",
            string __s when System.Text.RegularExpressions.Regex.IsMatch(__s, "^(?:^[a-z]+$)$") => "lowercase",
            string __s when System.Text.RegularExpressions.Regex.IsMatch(__s, "^(?:^[A-Z]+$)$") => "uppercase",
            var _ => "other",
        };
        Console.WriteLine($"'{input}' is {category}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
