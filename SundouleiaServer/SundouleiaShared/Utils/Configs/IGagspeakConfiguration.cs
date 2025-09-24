namespace SundouleiaShared.Utils.Configuration;

public interface ISundouleiaConfiguration
{
    T GetValueOrDefault<T>(string key, T defaultValue);
    T GetValue<T>(string key);
    string SerializeValue(string key, string defaultValue);
}
