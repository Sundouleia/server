using System.Text;

namespace SundouleiaShared.Utils.Configuration;

public class DiscordConfig : SundouleiaConfigBase
{
    public string BotToken              { get; set; } = string.Empty;
    public ulong? CkGuildId             { get; set; } = null; // To pull the other vanity roles from.
    public ulong? SundouleiaGuildId     { get; set; } = null;
    public ulong? ChannelForMessages    { get; set; } = null; // For the sundouleia server.
    public ulong? ChannelForReports     { get; set; } = null;
    public ulong? ChannelForCommands    { get; set; } = null;
    
    // Ck VanityRole IDs
    public Dictionary<ulong, string> CkVanityRoles { get; set; } = new Dictionary<ulong, string>();
    // Sundouleia VanityRole IDs
    public Dictionary<ulong, string> VanityRoles { get; set; } = new Dictionary<ulong, string>();

    /// <summary>
    ///     Outputs all info of the discords configuration to a string return.
    /// </summary>
    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine(base.ToString());
        sb.AppendLine($"{nameof(BotToken)} => {BotToken}");
        sb.AppendLine($"{nameof(MainServerAddress)} => {MainServerAddress}");
        sb.AppendLine($"{nameof(CkGuildId)} => {CkGuildId}");
        sb.AppendLine($"{nameof(SundouleiaGuildId)} => {SundouleiaGuildId}");
        sb.AppendLine($"{nameof(ChannelForMessages)} => {ChannelForMessages}");
        sb.AppendLine($"{nameof(ChannelForReports)} => {ChannelForReports}");
        sb.AppendLine($"{nameof(ChannelForCommands)} => {ChannelForCommands}");
        foreach (var role in CkVanityRoles)
            sb.AppendLine($"{nameof(CkVanityRoles)} => {role.Key} = {role.Value}");
        foreach (var role in VanityRoles)
            sb.AppendLine($"{nameof(VanityRoles)} => {role.Key} = {role.Value}");
        return sb.ToString();
    }
}