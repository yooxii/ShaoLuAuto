using System.Globalization;
using System.IO;
using WPFLocalizeExtension.Engine;

namespace ShaoLu.Services
{
    public static class LanguageService
    {
        private const string LangConfigPath = "current_language.txt";
        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 初始化语言：优先读取用户上次保存的语言，否则使用系统语言
        /// </summary>
        public static void Initialize()
        {
            string savedLang = File.Exists(LangConfigPath)
                ? File.ReadAllText(LangConfigPath).Trim()
                : CultureInfo.CurrentUICulture.Name;

            SetLanguage(savedLang);
        }

        /// <summary>
        /// 设置当前语言并持久化保存
        /// </summary>
        public static void SetLanguage(string cultureName)
        {
            try
            {
                LocalizeDictionary.Instance.Culture = new CultureInfo(cultureName);
                File.WriteAllText(LangConfigPath, cultureName);
                logger.Info("Language set to: {0}", cultureName);

            }
            catch (CultureNotFoundException)
            {
                logger.Error("Language {0} not found.", cultureName);
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
    }
}
