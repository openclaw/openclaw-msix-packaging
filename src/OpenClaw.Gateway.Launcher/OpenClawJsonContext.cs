using System.Text.Json.Serialization;

namespace OpenClaw.Gateway.Launcher;

[JsonSerializable(typeof(PayloadMetadata))]
[JsonSerializable(typeof(PayloadInventory))]
internal sealed partial class OpenClawJsonContext : JsonSerializerContext;
