using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ufs.Serializers;

public class FsPathJsonSerializer : JsonConverter<FsPath>
{
    public override FsPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var path = reader.GetString();
        if (path is not ['/', ..])
            path = '/' + path;
        return path.FsPath();
    }

    public override void Write(Utf8JsonWriter writer, FsPath value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
