using System;

namespace ShaoLu.Services
{
    /// <summary>
    /// Acbel 业务卡控弹窗：把厂商返回的错误代码与完整详情弹出来，方便现场截图报错。
    /// 「扫码」与「上传Acbel」两个步骤共用（例如 SET_BASIC_INFO 返回 0}B006-線別錯誤）。
    /// 未在 UI 线程时回到 UI 线程显示；没有 Application 时（单元测试）静默跳过。
    /// </summary>
    public static class AcbelDialogs
    {
        public static void ShowBusinessFail(string code, string detail, string titleFallback = "Acbel 业务卡控")
        {
            try
            {
                string title = Loc("AcbelBusinessFailTitle", titleFallback);
                string bodyTemplate = Loc("AcbelBusinessFailBody", "返回代码：{0}\n\n详情：\n{1}");
                string noCode = Loc("AcbelBusinessFailNoCode", "(未解析出代码)");
                string body = string.Format(bodyTemplate,
                    string.IsNullOrWhiteSpace(code) ? noCode : code,
                    detail ?? string.Empty);

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                dispatcher.Invoke(() => WPFDevelopers.Controls.MessageBox.Show(
                    body, title,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning));
            }
            catch
            {
                // 弹窗失败不影响步骤本身的结果
            }
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                string value = LanguageService.GetLocalizedString(key, fallback);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
