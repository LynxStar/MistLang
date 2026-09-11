using System;

string text = "Hello, world";
string hello = text[0..5];
string world = text[7..];
Console.WriteLine($"hello={hello}");
Console.WriteLine($"world={world}");
Console.WriteLine($"last={text[^1]}");
