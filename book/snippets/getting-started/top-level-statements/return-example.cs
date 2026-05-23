using Standard;
using System;

if (args.Length == 0)
{
    System.Console.WriteLine("Error: no arguments provided");
    return 1;
}
System.Console.WriteLine($"Processing: {args[0]}");
return 0;
