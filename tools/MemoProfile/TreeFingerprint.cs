using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MemoProfile;

internal sealed record TreeFingerprint(string Sha256, int NodeCount)
{
    private static readonly Dictionary<Type, (PropertyInfo Span, PropertyInfo Leading, PropertyInfo Trailing, MethodInfo Children)> Members = [];

    public static TreeFingerprint Of(object root)
    {
        var text = new StringBuilder();
        var count = 0;
        Walk(root, text, ref count);
        return new TreeFingerprint(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))), count);
    }

    private static void Walk(object node, StringBuilder text, ref int count)
    {
        var type = node.GetType();
        if(!Members.TryGetValue(type, out var members))
        {
            members = (type.GetProperty("Span")!, type.GetProperty("LeadingHiddenTokens")!, type.GetProperty("TrailingHiddenTokens")!, type.GetMethod("Children", Type.EmptyTypes)!);
            Members[type] = members;
        }

        var span = members.Span.GetValue(node)!;
        var spanType = span.GetType();
        count++;
        _ = text.Append(type.Name).Append('[')
            .Append(spanType.GetProperty("Start")!.GetValue(span)).Append(',')
            .Append(spanType.GetProperty("Length")!.GetValue(span)).Append(";h")
            .Append(((IEnumerable)members.Leading.GetValue(node)!).Cast<object>().Count()).Append(',')
            .Append(((IEnumerable)members.Trailing.GetValue(node)!).Cast<object>().Count()).Append("](");
        foreach(var child in (IEnumerable)members.Children.Invoke(node, null)!)
        {
            if(child is null)
            {
                _ = text.Append("null,");
                continue;
            }

            Walk(child, text, ref count);
            _ = text.Append(',');
        }

        _ = text.Append(')');
    }
}