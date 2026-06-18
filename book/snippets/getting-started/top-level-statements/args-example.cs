using System;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
System.Console.WriteLine($"Received {args.Length} arguments");
if (args.Length > 0)
{
    System.Console.WriteLine($"First argument: {args[0]}");
}
