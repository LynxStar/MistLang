using System;

var numbers = new[] { 10, 20, 30, 40, 50 };
var both = numbers[1..4];
var startOnly = numbers[2..];
var endOnly = numbers[..2];
var full = numbers[..];
Console.WriteLine($"both={both.Length}");
Console.WriteLine($"startOnly={startOnly.Length}");
Console.WriteLine($"endOnly={endOnly.Length}");
Console.WriteLine($"full={full.Length}");
