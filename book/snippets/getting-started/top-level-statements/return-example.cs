using Standard;

if (args.Length == 0)
{
    System.Console.WriteLine("Error: no arguments provided");
    System.Environment.ExitCode = 1;
    return;
}
System.Console.WriteLine($"Processing: {args[0]}");
System.Environment.ExitCode = 0;
return;
