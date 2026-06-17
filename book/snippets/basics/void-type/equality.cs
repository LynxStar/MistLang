using System;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
MistVoid a = MistVoid.Instance;
MistVoid b = MistVoid.Instance;
Console.WriteLine($"void == void: {a == b}");
Console.WriteLine($"void != void: {a != b}");
