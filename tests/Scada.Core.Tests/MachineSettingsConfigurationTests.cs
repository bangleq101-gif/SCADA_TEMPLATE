using System.Globalization;
using Scada.Core.Configuration;
using Scada.Core.MachineSettings;
using Xunit;

namespace Scada.Core.Tests;

public sealed class MachineSettingsConfigurationTests
{
    [Theory]
    [InlineData(MachineParameterValueType.Boolean, "true", "true")]
    [InlineData(MachineParameterValueType.Boolean, "false", "false")]
    [InlineData(MachineParameterValueType.Integer, "+0012", "12")]
    [InlineData(MachineParameterValueType.Decimal, "12.500", "12.5")]
    public void CodecNormalizesCanonicalInvariantText(MachineParameterValueType type, string input, string expected)
    {
        Assert.True(MachineParameterValueCodec.TryNormalizePersisted(type, input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void CodecUsesCurrentCultureForEditorInputAndInvariantForPersistence()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");

        Assert.True(MachineParameterValueCodec.TryNormalizeEditor(MachineParameterValueType.Decimal, "12,5", culture, out var normalized));
        Assert.Equal("12.5", normalized);
        Assert.False(MachineParameterValueCodec.TryNormalizeEditor(MachineParameterValueType.Decimal, "12.5", culture, out _));
        Assert.Equal("12,5", MachineParameterValueCodec.FormatForEditor(MachineParameterValueType.Decimal, "12.5", culture));
    }

    [Fact]
    public void ValidationFindsHiddenReadOnlyInvalidParameterByStableIdentity()
    {
        var options = new RuntimeOptions
        {
            MachineSettings = new MachineSettingsOptions
            {
                Pages =
                [
                    new MachineSettingsPageDefinition
                    {
                        Id = "line-01",
                        Title = "Line 01",
                        IsVisible = false,
                        Parameters =
                        [
                            new MachineParameterDefinition
                            {
                                Id = "speed",
                                Name = "Speed",
                                IsReadOnly = true,
                                IsVisible = false,
                                ValueType = MachineParameterValueType.Integer,
                                Value = "not-an-integer"
                            }
                        ]
                    }
                ]
            }
        };

        var issue = Assert.Single(RuntimeOptionsValidation.CollectIssues(options), item => item.Code == "MACHINE_PARAMETER_VALUE_INVALID");
        Assert.True(issue.IsBlocking);
        Assert.Equal("line-01/speed", issue.ObjectId);
    }

    [Theory]
    [InlineData("en-US", "12.5", "12.5")]
    [InlineData("de-DE", "12,5", "12.5")]
    public void DecimalEditorRoundTripsExplicitCultureWithoutAmbiguousFallback(string cultureName, string editorText, string canonical)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.True(MachineParameterValueCodec.TryNormalizeEditor(MachineParameterValueType.Decimal, editorText, culture, out var normalized));
        Assert.Equal(canonical, normalized);
        Assert.Equal(editorText, MachineParameterValueCodec.FormatForEditor(MachineParameterValueType.Decimal, normalized, culture));
    }

    [Fact]
    public void StringPersistenceKeepsExactTextAndValidationFindsAllStructuralErrors()
    {
        const string text = "  exact, text  ";
        Assert.True(MachineParameterValueCodec.TryNormalizePersisted(MachineParameterValueType.String, text, out var normalized));
        Assert.Equal(text, normalized);

        var options = new RuntimeOptions { MachineSettings = new MachineSettingsOptions { Pages = [new MachineSettingsPageDefinition { Id = "valid-page", Title = "Valid page", Parameters = [new MachineParameterDefinition { Id = "", Name = "", ValueType = (MachineParameterValueType)99, Value = "x", Min = 10, Max = 1, Order = -1 }, new MachineParameterDefinition { Id = "bad", Name = "Bad", ValueType = (MachineParameterValueType)99, Value = "x", Min = 10, Max = 1, Order = -1 }] }, new MachineSettingsPageDefinition { Id = "also-valid", Title = "", Order = -1 }, new MachineSettingsPageDefinition { Id = "", Title = "ignored" }] } };
        var codes = RuntimeOptionsValidation.CollectIssues(options).Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MACHINE_PAGE_ID_REQUIRED", codes);
        Assert.Contains("MACHINE_PAGE_TITLE_REQUIRED", codes);
        Assert.Contains("MACHINE_PAGE_ORDER_INVALID", codes);
        Assert.Contains("MACHINE_PARAMETER_ID_REQUIRED", codes);
    }

    [Fact]
    public void ValidationRejectsNumericBoundsAndOutOfRangePersistedValue()
    {
        var options = new RuntimeOptions { MachineSettings = new MachineSettingsOptions { Pages = [new MachineSettingsPageDefinition { Id = "p", Title = "P", Parameters = [new MachineParameterDefinition { Id = "flag", Name = "Flag", ValueType = MachineParameterValueType.Boolean, Value = "true", Min = 0 }, new MachineParameterDefinition { Id = "speed", Name = "Speed", ValueType = MachineParameterValueType.Integer, Value = "12", Min = 0, Max = 10 }] }] } };
        var codes = RuntimeOptionsValidation.CollectIssues(options).Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MACHINE_PARAMETER_BOUNDS_TYPE_INVALID", codes);
        Assert.Contains("MACHINE_PARAMETER_VALUE_RANGE_INVALID", codes);
    }
}
