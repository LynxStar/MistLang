using Standard;

string text = "Hello, world";
string hello = text[0..5];
string world = text[7..];
System.Console.WriteLine($"hello={hello}");
System.Console.WriteLine($"world={world}");
System.Console.WriteLine($"last={text[^1]}");
