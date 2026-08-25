using System;
using System.Reflection;

[assembly: AssemblyVersion("0.0.0.0")]

namespace Verse
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StaticConstructorOnStartup : Attribute
    {
    }

    public static class Log
    {
        public static void Message(string text) { }
        public static void Warning(string text) { }
        public static void Error(string text) { }
    }
}
