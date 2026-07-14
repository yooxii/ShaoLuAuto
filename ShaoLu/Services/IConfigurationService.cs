using System.Threading.Tasks;

namespace ShaoLu.Services
{
    public interface IConfigurationService
    {
        Task<T> GetSettingAsync<T>(string key, T defaultValue = default) where T : class;
        Task SaveSettingAsync<T>(string key, T value) where T : class;
        Task RemoveSettingAsync(string key);
    }

}