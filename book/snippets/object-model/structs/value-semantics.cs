using Standard;

Snippet.Color a = new Snippet.Color(255, 0, 0);
Snippet.Color b = a;
b.r = 0;
System.Console.WriteLine(System.FormattableString.Invariant($"a.r={a.r} b.r={b.r}"));
Snippet.Point p1 = new Snippet.Point(3, 4);
Snippet.Point p2 = p1;
