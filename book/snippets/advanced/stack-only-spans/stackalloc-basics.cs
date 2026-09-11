using System;

System.Span<int> buffer = stackalloc int[4];
buffer[0] = 10;
buffer[1] = 20;
buffer[2] = 30;
buffer[3] = 40;
Console.WriteLine($"Length={buffer.Length}");
Console.WriteLine($"buffer[0]={buffer[0]}");
Console.WriteLine($"buffer[3]={buffer[3]}");
int total = 0;
foreach (int v in buffer)
{
    total = total + v;
}
Console.WriteLine($"total={total}");
System.Span<int> mid = buffer.Slice(1, 2);
Console.WriteLine($"mid.Length={mid.Length}");
Console.WriteLine($"mid[0]={mid[0]}");
mid[0] = 99;
Console.WriteLine($"buffer[1]={buffer[1]}");
