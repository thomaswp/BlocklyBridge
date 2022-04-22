using System;

namespace BlocklyBridge
{
    public static class Logger
    {
        public static ILogger Implementation = new ConsoleLogger();

        public static void Log(object message)
        {
            Implementation.Log(message);
        }

        public static void Warn(object message)
        {
            Implementation.Warn(message);
        }
    }

    public interface ILogger
    {
        void Log(object message);
        void Warn(object message);
    }

    internal class ConsoleLogger : ILogger
    {
        public void Log(object message)
        {
            Console.WriteLine(message);
        }

        public void Warn(object message)
        {
            Console.WriteLine(message);
        }
    }

}
