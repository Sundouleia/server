using Microsoft.AspNetCore.SignalR;
using SundouleiaAPI.Hub;
using SundouleiaServer.Hubs;
using System.Collections.Concurrent;

namespace SundouleiaServer.Services;
#nullable enable
/// <summary>
///     Keeps track of and monitors the active connections to the various signalR groups. 
/// </summary>
public class RadarService
{
    /// <summary> 
    ///     Max. Players that can be in a world-territory chat at once. <para />
    ///     
    ///     No, this is not low because im worried about server load, it's because 
    ///     I don't want to repeat the mistake of SyncShells, and the long term 
    ///     negative impact they had on the community.
    /// </summary>
    public const int RadarChatCap = 25;

    private readonly ILogger<SystemInfoService> _logger;
    private readonly IHubContext<SundouleiaHub, ISundouleiaHub> _hubContext;

    // Similar to the main Hub's _userConnections, but instead is { connectionID, connectedGroupName }
    public static readonly ConcurrentDictionary<string, string> GroupConnections = new(StringComparer.Ordinal);
    public static readonly ConcurrentDictionary<string, int> GroupGauges = new(StringComparer.Ordinal);
    // Could honestly keep the above updated via metrics

    public RadarService(ILogger<SystemInfoService> logger, IHubContext<SundouleiaHub, ISundouleiaHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
        _logger.LogInformation("Radar Service Started.");
    }

    /// <summary>
    ///     Retrieves the HubContext Group name for the territory we are in. <para />
    ///     Can prevent this from going out of hand with soft-caps.
    /// </summary>
    public string GetGroupName(ushort worldId, ushort territoryId) => $"RadarChat_{worldId}_{territoryId}";

    /// <summary>
    ///     Joins the correct chat room based on the world and territory provided.
    /// </summary>
    public async Task JoinRadarChat(ushort world, ushort territory, string connectionId, string? uid = null)
    {
        // see if the connection ID is already in a group first, because they must leave it if so.
        if (GroupConnections.TryGetValue(connectionId, out string? existingGroup))
        {
            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, existingGroup).ConfigureAwait(false);
            // dec the count from that groups counter.
            if (GroupGauges.TryGetValue(existingGroup, out int existingCount) && existingCount > 0)
                GroupGauges[existingGroup] = existingCount - 1;
        }
        // Now join the group, and update the dictionary with the new status.
        var newGroupName = GetGroupName(world, territory);
        await _hubContext.Groups.AddToGroupAsync(connectionId, newGroupName).ConfigureAwait(false);
        GroupConnections[connectionId] = newGroupName;
        // Add the name if it doesn't exist, and increment the count regardless.
        GroupGauges.AddOrUpdate(newGroupName, 1, (_, count) => count + 1);
        _logger.LogDebug($"UserJoinChat:[{uid ?? connectionId}][{world}][{territory}]");
    }

    /// <summary>
    ///     Abort connection from the present radar chat if in one, and update the group connections state.
    /// </summary>
    public async Task LeaveRadarChat(string connectionId, string? uid = null)
    {
        // Attempt to get the group that we were present in. If none can be found, there is no group to leave.
        if (!GroupConnections.TryRemove(connectionId, out string? existingGroup))
            return;

        // Remove from the group, and update the dictionary.
        await _hubContext.Groups.RemoveFromGroupAsync(connectionId, existingGroup).ConfigureAwait(false);
        // dec the count from that groups counter.
        if (GroupGauges.TryGetValue(existingGroup, out int existingCount) && existingCount > 0)
            GroupGauges[existingGroup] = existingCount - 1;
        _logger.LogDebug($"UserLeaveChat:[{uid ?? connectionId}][{existingGroup}]");
    }

    public bool IsInChat(string connectionId)
        => GroupConnections.ContainsKey(connectionId);

    public int GetActiveGroupUsers(ushort world, ushort territory)
        => GroupGauges.TryGetValue(GetGroupName(world, territory), out int count) ? count : 0;
}
#nullable disable