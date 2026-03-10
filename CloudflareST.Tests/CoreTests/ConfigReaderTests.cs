using System.Text.Json;
using System.Threading.Tasks;
using CloudflareST.Core.Config;
using CloudflareST.Core;
using CloudflareST.Core.Config;
using Xunit;

namespace CloudflareST.Tests.CoreTests
{
    public class ConfigReaderTests
    {
        [Fact]
        public void JsonConfigReader_Parse_ValidJson()
        {
            var reader = new JsonConfigReader();
            var cfg = reader.Read("{\"Placeholder\":\"xyz\"}");
            Assert.NotNull(cfg);
            Assert.Equal("xyz", cfg.Placeholder);
        }

        [Fact]
        public void JsonConfigReader_Parse_InvalidJson_Returns_Default()
        {
            var reader = new JsonConfigReader();
            var cfg = reader.Read("{" );
            Assert.NotNull(cfg);
        }
    }
}
