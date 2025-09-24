using SundouleiaAPI.Enums;

namespace SundouleiaShared.Utils;

/// <summary> Represents a message sent to the client. </summary>
public record HardReconnectMessage(MessageSeverity Severity, string Message, ServerState State, string userUID);
