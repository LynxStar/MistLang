using System;

Span<int> buffer = stackalloc int[4];
buffer[0] = 10;
buffer[1] = 20;
buffer[2] = 30;
buffer[3] = 40;
System.Console.WriteLine($"Length={buffer.Length}");
System.Console.WriteLine($"buffer[0]={buffer[0]}");
System.Console.WriteLine($"buffer[3]={buffer[3]}");
int total = 0;
foreach (int v in buffer)
{
    total = total + v;
}
System.Console.WriteLine($"total={total}");
var mid = buffer.Slice(1, 2);
System.Console.WriteLine($"mid.Length={mid.Length}");
System.Console.WriteLine($"mid[0]={mid[0]}");
mid[0] = 99;
System.Console.WriteLine($"buffer[1]={buffer[1]}");
