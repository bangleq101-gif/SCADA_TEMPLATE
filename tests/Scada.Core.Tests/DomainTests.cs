using Scada.Core.Common;
using Scada.Core.Tags;
using Xunit;

namespace Scada.Core.Tests;

public sealed class DomainTests
{
    [Fact]
    public void RuntimeIdRejectsBlankValues()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeId(" "));
    }

    [Fact]
    public void RuntimeIdTrimsValue()
    {
        Assert.Equal("Runtime01", new RuntimeId(" Runtime01 ").Value);
    }

    [Fact]
    public void TagValueCarriesQualityAndTimestamp()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var value = new TagValue("T1", 12.5, TagQuality.Good, timestamp, 1);

        Assert.Equal(TagQuality.Good, value.Quality);
        Assert.Equal(timestamp, value.Timestamp);
    }
}
