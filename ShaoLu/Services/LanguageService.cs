using System;
using System.Globalization;
using System.IO;
using WPFLocalizeExtension.Engine;

namespace ShaoLu.Services
{
    public static class LanguageService
    {
        /// <summary>
        /// 语言配置文件目录：%AppData%\AutoShaoLu（与设置/统计/用户库保持同一目录约定）
        /// </summary>
        private static string LangConfigDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu");

        private static string LangConfigPath => Path.Combine(LangConfigDir, "current_language.txt");

        /// <summary>
        /// 旧版本把语言文件写在当前工作目录/程序目录；仅作为一次性迁移来源，不强制删除
        /// </summary>
        private static string[] LegacyLangConfigPaths => new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "current_language.txt"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "current_language.txt"),
        };

        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 初始化语言：优先读取用户上次保存的语言，否则使用系统语言。
        /// 读取失败（不存在/无权限/内容非法）时回退系统语言，绝不使启动崩溃。
        /// </summary>
        public static void Initialize()
        {
            string savedLang = ReadSavedLanguage() ?? CultureInfo.CurrentUICulture.Name;
            SetLanguage(savedLang);
        }

        /// <summary>
        /// 读取已保存的语言代码；读不到或内容非法返回 null（调用方回退系统语言）
        /// </summary>
        private static string ReadSavedLanguage()
        {
            // 1) 优先 AppData 中的配置
            try
            {
                if (File.Exists(LangConfigPath))
                {
                    string saved = File.ReadAllText(LangConfigPath).Trim();
                    if (!string.IsNullOrEmpty(saved)) return saved;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Read language config failed: {0}", LangConfigPath);
            }

            // 2) 旧位置（程序目录/当前工作目录）作为一次性迁移来源：读到后迁移到 AppData
            foreach (var legacyPath in LegacyLangConfigPaths)
            {
                try
                {
                    if (!File.Exists(legacyPath)) continue;

                    string legacy = File.ReadAllText(legacyPath).Trim();
                    if (!string.IsNullOrEmpty(legacy))
                    {
                        WriteSavedLanguage(legacy); // 迁移（失败只告警，旧文件保留）
                        logger.Info("Migrated language config from {0}", legacyPath);
                        return legacy;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Read legacy language config failed: {0}", legacyPath);
                }
            }

            return null;
        }

        /// <summary>
        /// 持久化语言代码到 AppData（原子写入；失败只告警，不影响本次语言切换）
        /// </summary>
        private static void WriteSavedLanguage(string cultureName)
        {
            string tempPath = LangConfigPath + ".tmp";
            try
            {
                if (!Directory.Exists(LangConfigDir))
                    Directory.CreateDirectory(LangConfigDir);

                File.WriteAllText(tempPath, cultureName);
                if (File.Exists(LangConfigPath))
                    File.Replace(tempPath, LangConfigPath, null);
                else
                    File.Move(tempPath, LangConfigPath);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Save language config failed: {0}", LangConfigPath);
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch { /* 清理临时文件失败可忽略 */ }
            }
        }

        /// <summary>
        /// 设置当前语言并持久化保存
        /// </summary>
        public static void SetLanguage(string cultureName)
        {
            try
            {
                LocalizeDictionary.Instance.Culture = new CultureInfo(cultureName);
                WriteSavedLanguage(cultureName);
                logger.Info("Language set to: {0}", cultureName);
            }
            catch (CultureNotFoundException)
            {
                // 文化名非法：回退系统语言，避免启动或切换语言时崩溃
                logger.Warn("Language {0} not found, fallback to system language.", cultureName);
                try
                {
                    LocalizeDictionary.Instance.Culture = CultureInfo.CurrentUICulture;
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Fallback language failed");
                }
            }
            catch (Exception ex)
            {
                // 其它异常同样不得让启动流程崩溃
                logger.Error(ex, "Set language failed: {0}", cultureName);
            }
        }

        /// <summary>
        /// 获取当前语言代码
        /// </summary>
        public static string GetCurrentLanguage()
        {
            return LocalizeDictionary.Instance.Culture.Name;
        }

        /// <summary>
        /// 获取本地化字符串, 如果找不到则返回key本身
        /// </summary>
        public static string GetLocalizedString(string key)
        {
            return LocalizeDictionary.Instance.GetLocalizedObject(key, null, null)?.ToString() ?? key;
        }

        /// <summary>
        /// 获取本地化字符串, 如果找不到则返回defaultValue
        /// </summary>
        public static string GetLocalizedString(string key, string defaultValue)
        {
            return LocalizeDictionary.Instance.GetLocalizedObject(key, null, null)?.ToString() ?? defaultValue;
        }

        /// <summary>
        /// 获取本地化字符串并按 {0}/{1} 占位符格式化；key 缺失时用 defaultValue 作为模板。
        /// </summary>
        public static string GetLocalizedString(string key, string defaultValue, params object[] args)
        {
            var value = LocalizeDictionary.Instance.GetLocalizedObject(key, null, null)?.ToString();
            if (string.IsNullOrEmpty(value))
                value = defaultValue;
            if (args == null || args.Length == 0)
                return value;
            try
            {
                return string.Format(CultureInfo.CurrentCulture, value, args);
            }
            catch (FormatException)
            {
                return value;
            }
        }
    }
}
