using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;

namespace SundouleiaServer.Services;

/// <summary>
///   Documents all chat channel history temporarily to help with reporting and fetching recent messages.
/// </summary>
/// <remarks> Shift from linked list to circular buffer from CkCommons, if possible. </remarks>
public class RadarChatService
{
    private const int MESSAGE_HISTORY_LIMIT = 50;
    private readonly Lock _recorderLock = new();

    // Records the most recent 50 chat messages from each channel.
    private readonly Dictionary<string, LinkedList<ChatlogMessage>> _chatHistory = [];
    private readonly Dictionary<string, DateTime> _recentActivity = []; // For pruning inactive channels, if needed.

    /// <summary>
    ///   Generic method, WIP <b> Does not currently function! </b>
    /// </summary>
    public ChatlogMessage LogMessage(SundChatKind kind, string chatId)
    {
        return default;
    }

    // Radar chat method
    public ChatlogMessage LogRadarChat(string chatId, SentRadarMessage msg)
    {
        var chatlogId = new ChatlogId(SundChatKind.Radar, chatId);
        var msgId = Guid.NewGuid().ToString("N"); // No dashes
        var timeSent = DateTime.UtcNow;
        // Build the chat message to log
        var logMsg = new ChatlogMessage(chatlogId, msgId, timeSent, msg.Sender, msg.Message);
        // Safely update via a lock
        lock (_recorderLock)
        {
            // If the entry cannot be found, create and append a new one.
            if (!_chatHistory.TryGetValue(chatId, out var history))
            {
                history = new LinkedList<ChatlogMessage>();
                _chatHistory[chatId] = history;
            }

            // Append to the end of the linked list
            var node = history.AddLast(logMsg);

            // Trim excess
            if (history.Count > MESSAGE_HISTORY_LIMIT)
                history.RemoveFirst();

            // Track last activity
            _recentActivity[chatId] = DateTime.UtcNow;
        }

        // ret the message if desired.
        return logMsg;
    }

    public IReadOnlyList<LoggedRadarChatMessage> GetRadarHistory(string chatId, int messageCount)
    {
        lock (_recorderLock)
        {
            // Ret empty list if the contents are not found
            if (!_chatHistory.TryGetValue(chatId, out var history))
                return [];

            // Compose what to return
            var toTake = Math.Min(messageCount, history.Count);
            var res = new LoggedRadarChatMessage[toTake];
            var node = history.Last;
            for (int i = toTake - 1; i >= 0 && node is not null; i--)
            {
                var msg = node.Value;
                res[i] = new(chatId, msg.MsgId, msg.TimeSentUTC, msg.Sender, msg.Message, RadarChatFlags.None);
                node = node.Previous!;
            }

            return res;
        }
    }

    /// <summary>
    ///   Useful for extracting details in regards to a chat report.
    /// </summary>
    /// <param name="chatId"> The chat to scan the details of </param>
    /// <param name="messageCount"> how many messages to extract, ordered by time sent from latest </param>
    /// <returns> the message history with <paramref name="messageCount"/> messages, or chathistory size (if less) </returns>
    public IReadOnlyList<ChatlogMessage> GetMessageHistory(string chatId, int messageCount)
    {
        lock (_recorderLock)
        {
            // Ret empty list if the contents are not found
            if (!_chatHistory.TryGetValue(chatId, out var history))
                return [];

            // ensure we get messages within the valid range.
            var toTake = Math.Min(messageCount, history.Count);
            var res = new ChatlogMessage[toTake];
            // fill the resulting history array by traversing the linked list from the end.
            var node = history.Last;
            for (int i = toTake - 1; i >= 0 && node is not null; i--)
            {
                res[i] = node.Value;
                node = node.Previous!;
            }

            return res;
        }
    }
}
