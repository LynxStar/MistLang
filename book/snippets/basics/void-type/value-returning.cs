using System;

static void DoNothing()
{
    return;
}
DoNothing();
Standard.Void result = Standard.Void.Instance;
Console.WriteLine($"DoNothing() returned: {result}");
