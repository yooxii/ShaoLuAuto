using ShaoLu.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public class SettingsService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 用户配置目录：%AppData%\AutoShaoLu（与烧录统计、用户数据库保持同一目录约定）
    /// </summary>
    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoShaoLu");

    private static string SettingsFilePath => Path.Combine(SettingsDir, "settings.json");

    /// <summary>
    /// 旧版本把 settings.json 写在程序目录；仅作为一次性迁移来源读取，不再写回
    /// </summary>
    private static string LegacySettingsFilePath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 异步加载设置。
    /// 读取失败（文件损坏/无权限/格式错误）时回退为默认设置，绝不把异常抛给启动流程。
    /// </summary>
    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            bool fromLegacy = false;
            string path = SettingsFilePath;
            if (!File.Exists(path) && File.Exists(LegacySettingsFilePath))
            {
                // 旧位置存在配置：一次性迁移来源
                path = LegacySettingsFilePath;
                fromLegacy = true;
            }

            if (!File.Exists(path))
                return new AppSettings();

            AppSettings settings;
            using (var stream = File.OpenRead(path))
            {
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                               .ConfigureAwait(false)
                           ?? new AppSettings();
            }

            if (fromLegacy)
            {
                // 读到旧位置的配置后迁移到 AppData；迁移失败只告警（不影响本次使用）
                try
                {
                    await SaveAsync(settings).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Migrate settings from {0} failed", path);
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Load settings failed, using default");
            return new AppSettings();
        }
    }

    /// <summary>
    /// 异步保存设置。
    /// 原子写入（先生成同目录临时文件，再 File.Replace 替换），避免写一半损坏配置；
    /// 返回是否保存成功，失败时写 Warn 日志而不是抛出异常。
    /// </summary>
    public static async Task<bool> SaveAsync(AppSettings settings)
    {
        string tempPath = SettingsFilePath + ".tmp";
        try
        {
            if (settings == null)
                settings = new AppSettings();

            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            // 1) 先写临时文件并落盘
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            // 2) 原子替换（同目录同卷，File.Replace 失败即整体失败，原文件保持不变）
            if (File.Exists(SettingsFilePath))
                File.Replace(tempPath, SettingsFilePath, null);
            else
                File.Move(tempPath, SettingsFilePath);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Save settings failed: {0}", SettingsFilePath);
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { /* 清理临时文件失败可忽略 */ }
            return false;
        }
    }
}
