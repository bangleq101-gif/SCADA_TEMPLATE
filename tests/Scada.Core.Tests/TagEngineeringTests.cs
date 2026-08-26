using Scada.Core.Configuration;
using Scada.Core.Tags;
using Xunit;

namespace Scada.Core.Tests;

public sealed class TagEngineeringTests
{
    [Fact]
    public void IdentityInt64TransformPreservesValuesBeyondDoublePrecision()
    {
        var definition = new TagDefinition
        {
            SourceDataType = TagDataType.Int64,
            DataType = TagDataType.Int64,
            Scale = 1d,
            Offset = 0d
        };
        const long rawValue = 9_007_199_254_740_993L;

        var transformed = TagValueTransformer.TryTransform(definition, rawValue, out var value, out var failure);

        Assert.True(transformed, failure);
        Assert.Equal(rawValue, Assert.IsType<long>(value));
    }

    [Fact]
    public void NumericSourceIsConvertedToCanonicalEngineeringDouble()
    {
        var definition = new TagDefinition
        {
            Id = "LEVEL_RAW",
            SourceDataType = TagDataType.Int32,
            DataType = TagDataType.Double,
            Scale = 0.1d,
            Offset = -20d
        };

        var transformed = TagValueTransformer.TryTransform(definition, 1_234, out var value, out var failure);

        Assert.True(transformed, failure);
        Assert.Equal(103.4d, Assert.IsType<double>(value), precision: 10);
    }

    [Theory]
    [InlineData(TagDataType.Boolean, true)]
    [InlineData(TagDataType.String, "Running")]
    public void NonNumericTagsRequireAndPreserveTheirExactValueType(TagDataType dataType, object rawValue)
    {
        var definition = new TagDefinition
        {
            Id = "STATE",
            SourceDataType = dataType,
            DataType = dataType,
            Scale = 1d,
            Offset = 0d
        };

        var transformed = TagValueTransformer.TryTransform(definition, rawValue, out var value, out var failure);

        Assert.True(transformed, failure);
        Assert.Equal(rawValue, value);
    }

    [Fact]
    public void LegacyNullSourceTypeUsesCanonicalTypeAndIdentityTransform()
    {
        var definition = new TagDefinition { Id = "LEGACY", DataType = TagDataType.Int64 };

        var transformed = TagValueTransformer.TryTransform(definition, 42L, out var value, out var failure);

        Assert.True(transformed, failure);
        Assert.Equal(42L, Assert.IsType<long>(value));
        Assert.Equal(TagDataType.Int64, definition.GetEffectiveSourceDataType());
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(double.NaN, 0d)]
    [InlineData(1d, double.PositiveInfinity)]
    public void NumericTransformRejectsInvalidScaleOrOffset(double scale, double offset)
    {
        var definition = new TagDefinition
        {
            Id = "BAD_SCALE",
            SourceDataType = TagDataType.Int32,
            DataType = TagDataType.Double,
            Scale = scale,
            Offset = offset
        };

        Assert.False(TagValueTransformer.TryTransform(definition, 1, out _, out var failure));
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Fact]
    public void NumericTransformRejectsLossyIntegralConversionInsteadOfRounding()
    {
        var definition = new TagDefinition
        {
            Id = "WHOLE",
            SourceDataType = TagDataType.Double,
            DataType = TagDataType.Int32,
            Scale = 1d,
            Offset = 0d
        };

        Assert.False(TagValueTransformer.TryTransform(definition, 12.5d, out _, out var failure));
        Assert.Contains("without rounding", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsIncompatibleOrInvalidEngineeringConfiguration()
    {
        var options = new RuntimeOptions();
        options.Devices.Add(new Scada.Core.Devices.DeviceDefinition { Id = "SIM01", DriverType = "Simulator" });
        options.Tags.Add(new TagDefinition
        {
            Id = "BOOL",
            Name = "BOOL",
            DeviceId = "SIM01",
            Address = "BOOL",
            SourceDataType = TagDataType.Boolean,
            DataType = TagDataType.Double,
            Scale = 0d,
            Offset = 1d
        });

        var issues = RuntimeOptionsValidation.CollectIssues(options);

        Assert.Contains(issues, issue => issue.Code == "TAG_ENGINEERING_TYPE_INCOMPATIBLE" && issue.IsBlocking);
        Assert.Contains(issues, issue => issue.Code == "TAG_ENGINEERING_TRANSFORM_NON_NUMERIC" && issue.IsBlocking);
    }
}
