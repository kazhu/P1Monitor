using P1Monitor.Model;
using System.Text.Json.Serialization;

namespace P1Monitor;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ObisMapping))]
[JsonSerializable(typeof(Dictionary<string, DeviceMappingDescriptor>))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
