namespace CloudflareST.Core.Interfaces
{
    public interface IOutputWriter
    {
        void Write(string message);
        void WriteLine(string message);
    }
}
