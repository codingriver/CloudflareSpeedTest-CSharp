using System;
using CloudflareST.Core.Interfaces;

namespace CloudflareST.Core.Output
{
    public class ConsoleOutputWriter : IOutputWriter
    {
        public void Write(string message) => Console.Write(message);
        public void WriteLine(string message) => Console.WriteLine(message);
    }
}
