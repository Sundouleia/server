using SundouleiaShared.Utils.Configuration;

namespace SundouleiaShared.Services;

public interface IConfigurationService<T> where T : class, ISundouleiaConfiguration
{
    bool IsMain { get; }
    T1 GetValue<T1>(string key);
    T1 GetValueOrDefault<T1>(string key, T1 defaultValue);
    string ToString();
}