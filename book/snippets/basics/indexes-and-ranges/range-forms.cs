using System;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] both = numbers[1..4];
int[] startOnly = numbers[2..];
int[] endOnly = numbers[..2];
int[] full = numbers[..];
Console.WriteLine($"both={both.Length}");
Console.WriteLine($"startOnly={startOnly.Length}");
Console.WriteLine($"endOnly={endOnly.Length}");
Console.WriteLine($"full={full.Length}");
