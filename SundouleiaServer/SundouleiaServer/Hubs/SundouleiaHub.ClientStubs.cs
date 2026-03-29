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

    // --- Moodles Integration Callbacks ---
    public Task Callback_PairLociDataUpdated(LociDataUpdate _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairLociStatusesUpdate(LociStatusesUpdate _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairLociPresetsUpdate(LociPresetsUpdate _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairLociStatusModified(LociStatusModified _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_PairLociPresetModified(LociPresetModified _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ApplyLociDataById(ApplyLociDataById _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ApplyLociStatus(ApplyLociStatus _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RemoveLociData(RemoveLociData _) => throw new PlatformNotSupportedException(UnsupportedMessage);

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
    public Task Callback_RadarChatMessage(LoggedRadarChatMessage _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarChatAddUpdateUser(RadarChatMember _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarAddUpdateUser(RadarMember _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarRemoveUser(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarGroupAddUpdateUser(RadarGroupMember _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_RadarGroupRemoveUser(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    public Task Callback_ChatMessageReceived(ReceivedChatMessage _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- User Status Update Callbacks ---
    public Task Callback_UserIsUnloading(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_UserOffline(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_UserOnline(OnlineUser _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_UserVanityUpdate(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);
    public Task Callback_ProfileUpdated(UserDto _) => throw new PlatformNotSupportedException(UnsupportedMessage);

    // --- InGame Account Verification ---
    public Task Callback_ShowVerification(VerificationCode _) => throw new PlatformNotSupportedException(UnsupportedMessage);
}