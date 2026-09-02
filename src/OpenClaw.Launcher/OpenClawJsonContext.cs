using System.Text.Json.Serialization;

namespace OpenClaw.Launcher;

[JsonSerializable(typeof(PayloadMetadata))]
internal sealed partial class OpenClawJsonContext : JsonSerializerContext;
