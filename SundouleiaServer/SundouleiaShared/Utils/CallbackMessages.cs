using SundouleiaAPI.Enums;

namespace SundouleiaShared.Utils;

/// <summary>
///     Represents a message sent to the client.
/// </summary>
public record ClientMessage(MessageSeverity Severity, string Message, string UID);

/// <summary> 
///     Represents a message sent to the client by force.
/// </summary>
public record HardReconnectMessage(MessageSeverity Severity, string Message, ServerState State, string userUID);