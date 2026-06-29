using System.Globalization;
using System.IO;
using WPFLocalizeExtension.Engine;

namespace ShaoLu.Services
{
    public static class LanguageService
    {
        private const string LangConfigPath = "current_language.txt";

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
            }
            catch (CultureNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"不支持的语言代码: {cultureName}");
            }
        }

        /// <summary>
        /// 获取当前语言代码
        /// </summary>
        public static string GetCurrentLanguage()
        {
            return LocalizeDictionary.Instance.Culture.Name;
        }
    }
}
