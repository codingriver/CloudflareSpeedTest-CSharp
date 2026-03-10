namespace CloudflareST.Core.Config
{
    using CloudflareST.Core;
    public interface IConfigReader
    {
        TestConfig Read(string source);
    }
}
