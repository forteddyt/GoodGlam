using System.Text.Json.Serialization;

namespace GoodGlam;

/// <summary>Serializes an enum by name and rejects its underlying numeric values on read.</summary>
internal sealed class StrictJsonStringEnumConverter<TEnum>()
    : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
