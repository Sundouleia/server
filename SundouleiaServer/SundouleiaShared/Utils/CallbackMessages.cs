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

// Non-Messagepack, Serializable ChatlogMessage for report serialization.
public readonly record struct JsonChatMsg(string MsgId, DateTime TimeSentUTC, string SenderUid, string SenderName, string Message);