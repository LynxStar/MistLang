using System;

var numbers = new[] { 10, 20, 30, 40, 50 };
var slice = numbers[1..4];
Console.WriteLine($"length={slice.Length}");
Console.WriteLine($"first={slice[0]}");
Console.WriteLine($"last={slice[^1]}");
slice[0] = 999;
Console.WriteLine($"sliceMutated={slice[0]}");
Console.WriteLine($"sourceUnchanged={numbers[1]}");
