using Scada.App.Services;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class TagTableCodecTests
{
    [Fact]
    public void ClipboardUsesExcelCompatibleTsvHeadersAndRoundTripsFields()
    {
        var source = new TagDefinition
        {
            Id = "T1",
            Name = "Pump\tRun",
            Description = "Line 1\r\nLine 2",
            DeviceId = "SIM01",
            Address = "A1",
            SourceDataType = TagDataType.Int32,
            DataType = TagDataType.Double,
            Scale = 0.1d,
            Offset = -20d,
            AccessMode = TagAccessMode.ReadWrite,
            Min = -1.5,
            Max = 100.25,
            Unit = "°C",
            HistoryEnabled = true,
            HistoryProfile = "FastAnalog",
            MqttPublishEnabled = true,
            MqttProfile = "FutureProfile",
            MqttTopicOverride = "line/\"pump\""
        };

        var clipboard = TagClipboardCodec.Export([source]);
        var roundTrip = Assert.Single(TagClipboardCodec.Import(clipboard));

        Assert.Contains("Id\tName\tDescription", clipboard, StringComparison.Ordinal);
        Assert.DoesNotContain("{", clipboard, StringComparison.Ordinal);
        Assert.Equal(source.Id, roundTrip.Id);
        Assert.Equal(source.Name, roundTrip.Name);
        Assert.Equal(source.Description, roundTrip.Description);
        Assert.Equal(source.SourceDataType, roundTrip.SourceDataType);
        Assert.Equal(source.DataType, roundTrip.DataType);
        Assert.Equal(source.Scale, roundTrip.Scale);
        Assert.Equal(source.Offset, roundTrip.Offset);
        Assert.Equal(source.AccessMode, roundTrip.AccessMode);
        Assert.Equal(source.Min, roundTrip.Min);
        Assert.Equal(source.Max, roundTrip.Max);
        Assert.Equal(source.MqttTopicOverride, roundTrip.MqttTopicOverride);
    }

    [Fact]
    public void CsvSupportsBomQuotedCommaEscapedQuotesMultilineAndTrailingEmptyFields()
    {
        var source = new TagDefinition
        {
            Id = "T1",
            Name = "Pump, \"Run\"",
            Description = "first\nsecond",
            DeviceId = "SIM01",
            Address = "A1",
            Unit = string.Empty,
            MqttTopicOverride = string.Empty
        };

        var csv = "\uFEFF" + CsvCodec.Export([source]);
        var roundTrip = Assert.Single(CsvCodec.Import(csv));

        Assert.Equal(source.Name, roundTrip.Name);
        Assert.Equal(source.Description, roundTrip.Description);
        Assert.Equal(string.Empty, roundTrip.Unit);
        Assert.Equal(string.Empty, roundTrip.MqttTopicOverride);
    }

    [Fact]
    public void CsvRejectsUnclosedQuotesAndConflictingHeaders()
    {
        Assert.Throws<FormatException>(() => CsvCodec.Import("Id,Name\nT1,\"unclosed"));
        Assert.Throws<FormatException>(() => CsvCodec.Import("Id,Name,Name\nT1,One,Two"));
    }

    [Fact]
    public void CsvRoundTripPreservesMultipleRowsAndBooleanEnumMetadata()
    {
        var source = new[]
        {
            new TagDefinition { Id = "T1", Name = "One", DeviceId = "SIM01", Address = "A1", Enabled = false },
            new TagDefinition { Id = "T2", Name = "Two", DeviceId = "SIM01", Address = "A2", DataType = TagDataType.Int32, AccessMode = TagAccessMode.ReadWrite }
        };

        var imported = CsvCodec.Import(CsvCodec.Export(source));

        Assert.Equal(2, imported.Count);
        Assert.False(imported[0].Enabled);
        Assert.Equal(TagDataType.Int32, imported[1].DataType);
        Assert.Equal(TagAccessMode.ReadWrite, imported[1].AccessMode);
    }

    [Fact]
    public void LegacyTableWithoutEngineeringColumnsUsesIdentitySourceConfiguration()
    {
        var imported = CsvCodec.Import("Id,Name,DeviceId,Address,DataType\nT1,Level,SIM01,A1,Double\n");

        var tag = Assert.Single(imported);

        Assert.Equal(TagDataType.Double, tag.DataType);
        Assert.Equal(TagDataType.Double, tag.SourceDataType);
        Assert.Equal(1d, tag.Scale);
        Assert.Equal(0d, tag.Offset);
    }
}
