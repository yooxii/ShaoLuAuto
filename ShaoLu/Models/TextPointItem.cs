using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ShaoLu.Models
{
    /// <summary>
    /// 文本获取点定位方式
    /// </summary>
    public enum TextPointSource
    {
        /// <summary>屏幕绝对坐标（逻辑像素）</summary>
        Absolute,
        /// <summary>图像相对定位：先在屏幕上匹配裁剪图，再按点击位置偏移取文本</summary>
        Relative,
    }

    /// <summary>
    /// 文本获取点：获取输入步骤（ScreenText 模式）按列表顺序逐点获取文本，结果以换行合并
    /// </summary>
    public class TextPointItem : ObservableObject
    {
        /// <summary>唯一标识（裁剪图文件名 {Id}.png）</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        private TextPointSource _source = TextPointSource.Absolute;
        private double _x;
        private double _y;
        private string _imagePath;
        private string _croppedImageName;
        private System.Windows.Rect _croppedRect = new();
        private List<Point> _clickOffsets = new();
        private double _similarityThreshold = 0.85;

        /// <summary>定位方式</summary>
        public TextPointSource Source { get => _source; set => SetProperty(ref _source, value); }

        /// <summary>绝对坐标 X（逻辑像素，仅 Absolute 有效）</summary>
        public double X { get => _x; set { if (SetProperty(ref _x, value)) OnPropertyChanged(nameof(Summary)); } }

        /// <summary>绝对坐标 Y（逻辑像素，仅 Absolute 有效）</summary>
        public double Y { get => _y; set { if (SetProperty(ref _y, value)) OnPropertyChanged(nameof(Summary)); } }

        /// <summary>原图绝对路径（仅记录，不打包）</summary>
        public string ImagePath { get => _imagePath; set => SetProperty(ref _imagePath, value); }

        /// <summary>裁剪图在包内的相对路径，如 "images/{Id}.png"</summary>
        public string CroppedImageName { get => _croppedImageName; set { if (SetProperty(ref _croppedImageName, value)) OnPropertyChanged(nameof(Summary)); } }

        /// <summary>裁剪区域（原图像素坐标）</summary>
        public System.Windows.Rect CroppedRect { get => _croppedRect; set => SetProperty(ref _croppedRect, value); }

        /// <summary>点击位置偏移（相对裁剪区左上角，图像像素）；空列表表示取裁剪图中心</summary>
        public List<Point> ClickOffsets { get => _clickOffsets; set => SetProperty(ref _clickOffsets, value); }

        /// <summary>图像匹配相似度阈值</summary>
        public double SimilarityThreshold { get => _similarityThreshold; set => SetProperty(ref _similarityThreshold, value); }

        /// <summary>所有定位方式（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<TextPointSource> Sources { get; } = new() { TextPointSource.Absolute, TextPointSource.Relative };

        /// <summary>运行时从工作目录解析的裁剪图完整路径</summary>
        [JsonIgnore]
        public string CroppedImageFullPath
        {
            get
            {
                string workDir = Utils.SingletonLocator.Main?.StepImageWorkDir;
                if (string.IsNullOrEmpty(CroppedImageName) || string.IsNullOrEmpty(workDir)) return null;
                return System.IO.Path.Combine(workDir, CroppedImageName);
            }
        }

        /// <summary>解析原图路径：优先 ImagePath，失效时回退到工作目录内打包的原图（约定名 images/{Id}_src.*）</summary>
        public string ResolveSourceImagePath()
        {
            if (!string.IsNullOrEmpty(ImagePath) && System.IO.File.Exists(ImagePath)) return ImagePath;
            try
            {
                string workDir = Utils.SingletonLocator.Main?.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir)) return null;
                string imagesDir = System.IO.Path.Combine(workDir, "images");
                if (!System.IO.Directory.Exists(imagesDir)) return null;
                return System.IO.Directory.GetFiles(imagesDir, $"{Id}_src.*").FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>UI 显示的设置状态摘要</summary>
        [JsonIgnore]
        public string Summary
        {
            get
            {
                if (Source == TextPointSource.Absolute)
                {
                    return X == 0 && Y == 0
                        ? LanguageService.GetLocalizedString("OCR_NoRegion", "未选择位置")
                        : $"X:{X:F0}, Y:{Y:F0}";
                }
                return string.IsNullOrEmpty(CroppedImageName)
                    ? LanguageService.GetLocalizedString("No_img_Warning", "No picture selected")
                    : LanguageService.GetLocalizedString("Input_RelativeReady", "Configured");
            }
        }

        public TextPointItem Clone()
        {
            return new TextPointItem
            {
                Id = Id,
                Source = Source,
                X = X,
                Y = Y,
                ImagePath = ImagePath,
                CroppedImageName = CroppedImageName,
                CroppedRect = new System.Windows.Rect(CroppedRect.X, CroppedRect.Y, CroppedRect.Width, CroppedRect.Height),
                ClickOffsets = ClickOffsets?.Select(p => new Point(p.X, p.Y)).ToList() ?? new List<Point>(),
                SimilarityThreshold = SimilarityThreshold,
            };
        }
    }
}
