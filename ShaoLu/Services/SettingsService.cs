using ShaoLu.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public class SettingsService
{
    private static readonly string SettingsFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 异步加载设置
    /// </summary>
    public static async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsFilePath))
            return new AppSettings();

        using var stream = File.OpenRead(SettingsFilePath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
               ?? new AppSettings();
    }

    /// <summary>
    /// 异步保存设置
    /// </summary>
    public static async Task SaveAsync(AppSettings settings)
    {
        using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }
}