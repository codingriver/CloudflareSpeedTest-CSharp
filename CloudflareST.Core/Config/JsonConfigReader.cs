using System.Text.Json;

namespace CloudflareST.Core.Config
{
    public class JsonConfigReader : IConfigReader
    {
        public TestConfig Read(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return new TestConfig();
            }
            try
            {
                return JsonSerializer.Deserialize<TestConfig>(source) ?? new TestConfig();
            }
            catch
            {
                // Fallback to default config on parse errors
                return new TestConfig();
            }
        }
    }
}
