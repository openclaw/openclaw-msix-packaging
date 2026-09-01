using System.Text.Json.Serialization;

namespace OpenClaw.Launcher;

[JsonSerializable(typeof(PayloadMetadata))]
[JsonSerializable(typeof(PayloadInventory))]
internal sealed partial class OpenClawJsonContext : JsonSerializerContext;
