using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GoodGlam.Tests;

public class EnumJsonSerializationTests
{
    public static TheoryData<Type, object> EnumValues
    {
        get
        {
            var values = new TheoryData<Type, object>();
            foreach (var type in typeof(Configuration).Assembly.GetTypes().Where(type => type.IsEnum))
                values.Add(type, Enum.GetValues(type).GetValue(0)!);
            return values;
        }
    }

    [Theory]
    [MemberData(nameof(EnumValues))]
    public void Serializes_enums_as_strings(Type type, object value)
    {
        var json = JsonSerializer.Serialize(value, type);

        json.Should().Be($"\"{value}\"");
    }

    [Theory]
    [MemberData(nameof(EnumValues))]
    public void Deserializes_string_enums(Type type, object value)
    {
        var deserialized = JsonSerializer.Deserialize($"\"{value}\"", type);

        deserialized.Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(EnumValues))]
    public void Rejects_numeric_enums(Type type, object _)
    {
        var deserialize = () => JsonSerializer.Deserialize("0", type);

        deserialize.Should().Throw<JsonException>();
    }
}
