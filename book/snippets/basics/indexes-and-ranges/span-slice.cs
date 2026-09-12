using Standard;

System.Span<int> buffer = stackalloc int[5];
buffer[0] = 10;
buffer[1] = 20;
buffer[2] = 30;
buffer[3] = 40;
buffer[4] = 50;
System.Span<int> view = buffer[1..4];
System.Console.WriteLine($"viewLength={view.Length}");
System.Console.WriteLine($"view0={view[0]}");
view[0] = 99;
System.Console.WriteLine($"buffer1={buffer[1]}");
