using System;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
Index fromEnd = ^2;
Console.WriteLine($"valueAtStored={numbers[fromEnd]}");
Range window = 0..2;
int[] slice = numbers[window];
Console.WriteLine($"slice0={slice[0]}");
Console.WriteLine($"slice1={slice[1]}");
