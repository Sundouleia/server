using System.Text;

namespace SundouleiaShared.Utils.Configuration;

public class AuthServiceConfig : SundouleiaConfigBase
{
    public int FailedAuthForTempBan { get; set; } = 20;
    public int TempBanDurationInMinutes { get; set; } = 1;
    public List<string> WhitelistedIps { get; set; } = new();

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine(base.ToString());
        sb.AppendLine($"{nameof(RedisPool)} => {RedisPool}");
        return sb.ToString();
    }
}
