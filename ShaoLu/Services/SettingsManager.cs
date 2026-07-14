using ShaoLu.Models;
using System.Threading.Tasks;

namespace ShaoLu.Services
{
    public class SettingsManager(IConfigurationService configService)
    {
        private readonly IConfigurationService _configService = configService;

        public async Task<AppSettings> LoadAppSettingsAsync()
        {
            // 可以从单个大对象获取，也可以分别获取
            return await _configService.GetSettingAsync<AppSettings>("AppSettings", new AppSettings());
        }

        public async Task SaveAppSettingsAsync(AppSettings settings)
        {
            await _configService.SaveSettingAsync("AppSettings", settings);
        }
    }
}
