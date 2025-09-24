namespace SundouleiaShared.Metrics;

/// <summary> 
///     *insert some funny joke about mega corps taking all your data here*
/// </summary>
public class MetricsAPI
{
    // info about connection.
    public const string CounterInitializedConnections = "sund_initialized_connections";
    public const string GaugeConnections = "sund_connections";
    public const string GaugeAuthorizedConnections = "sund_authorized_connections";
    public const string GaugeAvailableWorkerThreads = "sund_available_thread_pool";
    public const string GaugeAvailableIOWorkerThreads = "sund_available_thread_pool_io";

    // Info about users.
    public const string CounterAuthRequests = "sund_authentication_requests";
    public const string CounterAuthSuccess = "sund_authentication_success";
    public const string CounterAuthFailed = "sund_authentication_failed";
    public const string GaugeUsersRegistered = "sund_users_registered";
    public const string CounterDeletedVerifiedUsers = "sund_users_registered_deleted";
    public const string GaugePairings = "sund_pairs";

    // Reporting and chatting.
    public const string CounterProfileUpdates = "sund_profile_updates";
    public const string CounterReportsCreatedProfile = "sund_reports_created_profile";
    public const string CounterReportsCreatedChat = "sund_reports_created_chat";
    public const string CounterReportsCreatedRadar = "sund_reports_created_radar";

    // IPC
    public const string CounterDataUpdateAll = "sund_data_update_all";
    public const string CounterDataUpdateMods = "sund_data_update_mods";
    public const string CounterDataUpdateAppearance = "sund_data_update_appearance";

    // Mod Updates
    public const string GaugeFilesTotal = "sund_files_total";

    // Requests
    public const string GaugeRequestsPending = "sund_requests_pending";
    public const string CounterRequestsCreated = "sund_requests_created";
    public const string CounterRequestsAccepted = "sund_requests_accepted";
    public const string CounterRequestsRejected = "sund_requests_rejected";
    public const string CounterRequestsExpired = "sund_requests_expired";
    // maybe something for temp if we want to split it, but otherwise no.

    // Permissions
    public const string CounterPermissionUpdateGlobal = "sund_permission_update_global";
    public const string CounterPermissionUpdateUnique = "sund_permission_update_unique";
}