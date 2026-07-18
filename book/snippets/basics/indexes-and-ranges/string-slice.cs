using System;

var text = "Hello, world";
var hello = text[0..5];
var world = text[7..];
Console.WriteLine($"hello={hello}");
Console.WriteLine($"world={world}");
Console.WriteLine($"last={text[^(1)]}");
