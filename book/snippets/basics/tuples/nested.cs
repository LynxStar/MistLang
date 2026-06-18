using System;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
((int, int), string) nested = ((1, 2), "label");
Console.WriteLine($"inner=({nested.Item1.Item1}, {nested.Item1.Item2}) outer={nested.Item2}");
