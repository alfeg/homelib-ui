using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MyHomeLibServer.Data.Services;

public static class ValueConversionExtensions
{
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> propertyBuilder) 
    {
        ValueConverter<T, string> converter = new ValueConverter<T, string>
        (
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null) ?? default
        );

        ValueComparer<T> comparer = new ValueComparer<T>
        (
            (l, r) => JsonSerializer.Serialize(l, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(r, (JsonSerializerOptions?)null),
            v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!
        );

        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueConverter(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);

        return propertyBuilder;
    }
}