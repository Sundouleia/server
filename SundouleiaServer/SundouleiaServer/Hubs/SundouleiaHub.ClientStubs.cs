using SundouleiaAPI.Enums;
using SundouleiaAPI.Network;

namespace SundouleiaServer.Hubs;

/// <summary>
///     Elements of the SundouleiaHub intended for the client-side returns. <para />
///     Any calls to this on the server are not supported for the platform.
/// </summary>
public partial class SundouleiaHub
{
	private const string UnsupportedMessage = "Calling Client-Side method on server not supported";
	public Task Callback_ServerMessage(MessageSeverity _, string __) => throw new PlatformNotSupportedException(UnsupportedMessage);
	public Task Callback_HardReconnectMessage(MessageSeverity _, string __, ServerState ___) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarUserFlagged(string _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ServerInfo(ServerInfoResponse _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- Pair/Request Callbacks ---
    public Task Callback_AddPair(UserPair _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RemovePair(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PersistPair(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_AddRequest(SundesmoRequest _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RemoveRequest(SundesmoRequest _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // -- Moderation Utility Callbacks ---
    public Task Callback_Blocked(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_Unblocked(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- Moodles Integration Callbacks ---
    public Task Callback_PairMoodleDataUpdated(MoodlesDataUpdate _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairMoodleStatusesUpdate(MoodlesStatusesUpdate _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairMoodlePresetsUpdate(MoodlesPresetsUpdate _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairMoodleStatusModified(MoodlesStatusModified _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairMoodlePresetModified(MoodlesPresetModified _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ApplyMoodleId(ApplyMoodleId _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ApplyMoodleStatus(ApplyMoodleStatus _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RemoveMoodleId(RemoveMoodleId _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- Data Update Callbacks ---
    public Task Callback_IpcUpdateFull(IpcUpdateFull _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_IpcUpdateMods(IpcUpdateMods _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_IpcUpdateOther(IpcUpdateOther _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_IpcUpdateSingle(IpcUpdateSingle _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ChangeGlobalPerm(ChangeGlobalPerm _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ChangeAllGlobal(ChangeAllGlobal _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ChangeUniquePerm(ChangeUniquePerm _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ChangeUniquePerms(ChangeUniquePerms _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ChangeAllUnique(ChangeAllUnique _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- Radar Callbacks ---
    public Task Callback_RadarAddUpdateUser(OnlineUser _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarRemoveUser(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarChat(RadarChatMessage _) => throw new PlatformNotSupportedException(UnsupportedMessage);


    // --- User Status Update Callbacks ---
    public Task Callback_UserIsUnloading(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_UserOffline(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_UserOnline(OnlineUser _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ProfileUpdated(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- InGame Account Verification ---
    public Task Callback_ShowVerification(VerificationCode _) => throw new PlatformNotSupportedException(UnsupportedMessage);
}