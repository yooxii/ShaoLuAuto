using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ShaoLu.Services.Ocr
{
    /// <summary>
    /// OCR 扩展宿主：从 <c>extensions\Ocr</c> 加载 OCR 扩展并对外提供可用性状态与识别入口。
    ///
    /// 为什么要扩展：Tesseract 的 tesseract55.dll / leptonica 与 tessdata 语言包一共接近 100 MB，
    /// 而只用图像识别的用户用不到 OCR。把 OCR 拆成单独发布的扩展后，主体包变小，
    /// 未安装扩展的机器上“获取输入（OCR 模式）”“图像编辑里设置 OCR 区域”等功能会自动禁用。
    /// </summary>
    public static class OcrExtensionHost
    {
        /// <summary>扩展所在目录名（相对程序目录）</summary>
        public const string ExtensionsFolderName = "extensions";

        /// <summary>OCR 扩展的子目录名</summary>
        public const string ExtensionFolderName = "Ocr";

        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly object _sync = new object();

        private static IOcrExtensionFactory _factory;
        private static IOcrEngine _engine;
        private static bool _initialized;
        private static string _unavailableReason;
        private static bool _assemblyResolveRegistered;

        /// <summary>程序目录下的 extensions 目录</summary>
        public static string ExtensionsRoot => Path.Combine(AppContext.BaseDirectory, ExtensionsFolderName);

        /// <summary>OCR 扩展目录（<c>{程序目录}\extensions\Ocr</c>）；单元测试可用 Override 指到构建输出目录</summary>
        public static string ExtensionDirectory =>
            string.IsNullOrEmpty(ExtensionDirectoryOverride)
                ? Path.Combine(ExtensionsRoot, ExtensionFolderName)
                : ExtensionDirectoryOverride;

        /// <summary>测试用：把扩展目录指到别处（正常运行时为 null）</summary>
        internal static string ExtensionDirectoryOverride { get; set; }

        /// <summary>OCR 功能是否可用（扩展已安装，且引擎已成功初始化）</summary>
        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                lock (_sync)
                    return _factory != null && _engine != null;
            }
        }

        /// <summary>不可用原因（可用时为空字符串）</summary>
        public static string UnavailableReason
        {
            get
            {
                EnsureInitialized();
                lock (_sync)
                    return IsAvailableLocked ? string.Empty : (_unavailableReason ?? string.Empty);
            }
        }

        /// <summary>引擎/扩展显示名（不可用时为空字符串）</summary>
        public static string Description
        {
            get
            {
                EnsureInitialized();
                lock (_sync)
                    return IsAvailableLocked ? $"{_factory.Name} {_factory.Version}".Trim() : string.Empty;
            }
        }

        private static bool IsAvailableLocked => _factory != null && _engine != null;

        /// <summary>
        /// 扫描扩展目录并准备扩展入口。幂等；程序启动时调用一次，其它位置按需自动调用。
        /// </summary>
        public static void Initialize()
        {
            EnsureInitialized();
        }

        private static void EnsureInitialized()
        {
            lock (_sync)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    RegisterAssemblyResolve();
                    _factory = LoadFactory();
                    if (_factory == null)
                    {
                        _unavailableReason ??= LanguageService.GetLocalizedString(
                            "Ocr_ExtensionMissing",
                            "OCR 功能需要先安装 OCR 扩展：把扩展包里的 Ocr 文件夹放到程序目录的 extensions 下。");
                        logger.Info("OCR extension not available: {0}", _unavailableReason);
                    }
                    else
                    {
                        // 启动时就把引擎建起来：扩展放了但语言数据/原生库不全的情况要在这里暴露，
                        // 界面上的“OCR 可用/禁用”才是准确的
                        _engine = _factory.CreateEngine();
                        logger.Info("OCR extension loaded: {0} {1} ({2})",
                            _factory.Name, _factory.Version, ExtensionDirectory);
                    }
                }
                catch (Exception ex)
                {
                    _engine = null;
                    _unavailableReason = ex.Message;
                    logger.Error(ex, "Load OCR extension failed: {0}", ExtensionDirectory);
                }
            }
        }

        /// <summary>
        /// 诊断扩展是否带了原生库/语言数据：只检查文件存在性，不创建引擎
        /// （用于界面提示“扩展已放好但文件不全”）。
        /// </summary>
        public static bool IsExtensionDirectoryComplete()
        {
            try
            {
                string dir = ExtensionDirectory;
                if (!Directory.Exists(dir)) return false;
                if (!Directory.GetFiles(dir, "*.dll").Any()) return false;
                string tessData = Path.Combine(dir, "tessdata");
                return Directory.Exists(tessData) && Directory.GetFiles(tessData, "*.traineddata").Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 识别一张 PNG 图片。扩展不可用或识别失败时返回 null（调用方按“无文本”处理并记录日志）。
        /// </summary>
        public static string RecognizePng(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
                return null;

            EnsureInitialized();
            lock (_sync)
            {
                if (_factory == null || _engine == null) return null;
                try
                {
                    return _engine.RecognizePng(pngBytes)?.Trim();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "OCR recognize failed");
                    return null;
                }
            }
        }

        /// <summary>释放引擎（退出程序时调用）</summary>
        public static void Shutdown()
        {
            lock (_sync)
            {
                try
                {
                    _engine?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Dispose OCR engine failed");
                }
                _engine = null;
            }
        }

        /// <summary>扫描扩展目录，返回第一个能实例化的扩展入口；找不到返回 null</summary>
        private static IOcrExtensionFactory LoadFactory()
        {
            string dir = ExtensionDirectory;
            if (!Directory.Exists(dir))
            {
                _unavailableReason = LanguageService.GetLocalizedString(
                    "Ocr_ExtensionMissing",
                    "OCR 功能需要先安装 OCR 扩展：把扩展包里的 Ocr 文件夹放到程序目录的 extensions 下。");
                return null;
            }

            // 只扫描托管扩展 dll：原生库（tesseract55.dll 等）和语言数据不参与
            var candidates = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p).StartsWith("ShaoLu.Ocr", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var dll in candidates)
            {
                try
                {
                    Assembly asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.IsAbstract || !typeof(IOcrExtensionFactory).IsAssignableFrom(type))
                            continue;
                        if (type.GetConstructor(Type.EmptyTypes) == null)
                            continue;

                        var factory = (IOcrExtensionFactory)Activator.CreateInstance(type);
                        logger.Info("OCR extension factory found: {0} in {1}", type.FullName, dll);
                        return factory;
                    }
                }
                catch (Exception ex)
                {
                    // 单个 dll 加载失败不影响继续找下一个（扩展目录里可能还放着原生依赖）
                    logger.Warn(ex, "Skip OCR extension candidate: {0}", dll);
                }
            }

            _unavailableReason = LanguageService.GetLocalizedString(
                "Ocr_ExtensionBroken",
                "OCR 扩展目录存在，但没有可用的扩展（检查扩展包是否完整解压到 extensions\\Ocr）。");
            return null;
        }

        /// <summary>
        /// 扩展目录里的托管依赖（TesseractOCR.dll 等）从扩展目录解析。
        /// LoadFrom 上下文通常能找到同目录依赖，这里再兜一层，避免宿主已加载版本不一致导致加载失败。
        /// </summary>
        private static void RegisterAssemblyResolve()
        {
            if (_assemblyResolveRegistered) return;
            _assemblyResolveRegistered = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string dir = ExtensionDirectory;
                    if (!Directory.Exists(dir)) return null;

                    string simpleName = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(simpleName)) return null;

                    string candidate = Path.Combine(dir, simpleName + ".dll");
                    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                }
                catch
                {
                    return null;
                }
            };
        }

        #region 单元测试支持

        /// <summary>测试用：直接注入扩展入口（不写磁盘扩展目录）</summary>
        internal static void SetFactoryForTests(IOcrExtensionFactory factory)
        {
            lock (_sync)
            {
                try { _engine?.Dispose(); } catch { /* 测试清理忽略 */ }
                _factory = factory;
                _engine = null;
                _initialized = true;
                _unavailableReason = null;

                if (factory == null)
                {
                    _unavailableReason = "test: no factory";
                    return;
                }

                try
                {
                    _engine = factory.CreateEngine();
                }
                catch (Exception ex)
                {
                    _unavailableReason = ex.Message;
                }
            }
        }

        /// <summary>测试用：恢复未初始化状态，下次访问重新扫描扩展目录</summary>
        internal static void ResetForTests()
        {
            lock (_sync)
            {
                try { _engine?.Dispose(); } catch { /* 测试清理忽略 */ }
                _engine = null;
                _factory = null;
                _initialized = false;
                _unavailableReason = null;
                ExtensionDirectoryOverride = null;
            }
        }

        #endregion
    }
}
