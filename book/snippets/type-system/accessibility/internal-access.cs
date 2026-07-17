using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Logger
{
    internal string prefix;

    public Logger(string prefix)
    {
        this.prefix = prefix;
    }

    public string Format(string message)
    {
        return $"[{prefix}] {message}";
    }
}
public class LoggerFactory
{
    public static string GetPrefix(Logger logger)
    {
        return logger.prefix;
    }

    public static LoggerFactory __CreateDefault()
    {
        var inst = new LoggerFactory();
        return inst;
    }
}
