using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ShaoLu.Viewmodels.AutomationStep
{
    public class ImageRecognition : ImageRecognitionBase
    {
        public override AutomationStepBase Clone()
        {
            var clonedImg = (CroppedImg as BitmapSource)?.Clone();
            var res = new ImageRecognition()
            {
                Name = Name,
                Description = Description,
                Type = Type,
                IsNeed = IsNeed,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                WaitTime = WaitTime,
                ImagePath = ImagePath,
                CroppedImageName = CroppedImageName,
                CroppedRect = new(CroppedRect.X, CroppedRect.Y, CroppedRect.Width, CroppedRect.Height),
                ClickThumbs = ClickThumbs?.Select(t => t.Clone()).ToList(),
                SimilarityThreshold = SimilarityThreshold,
            };
            // 裁剪图只做内存克隆：克隆/快照是高频路径，不再每次都写 PNG（落盘由保存动作负责）
            if (clonedImg != null)
                res.SetCroppedImageInMemory(clonedImg);
            return res;
        }

        public override Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    public abstract partial class ImagesRecognitionBase : AutomationStepBase
    {

        private ObservableCollection<ImageRecognition> _images = [new ImageRecognition()];
        public ObservableCollection<ImageRecognition> Images { get => _images; set => SetProperty(ref _images, value); }

        private bool _oneByOne = false;
        /// <summary>
        /// 逐个匹配/点击（保留字段：当前没有 UI 入口，运行时也不参与逻辑，
        /// 仅为兼容旧数据保留读写，避免旧文件反序列化丢字段；不要据此认为它已生效）
        /// </summary>
        public bool OneByOne { get => _oneByOne; set => SetProperty(ref _oneByOne, value); }

        [RelayCommand]
        public void AddImage()
        {
            if (_images != null)
                Images.Add(new ImageRecognition());
        }

        [RelayCommand]
        public void DelImage()
        {
            if (_images != null && _images.Count > 1)
                Images.RemoveAt(Images.Count - 1);
        }
    }

    public class ClickImagesStep : ImagesRecognitionBase
    {
        private int _clicks = 1;
        private double _clickGap = 0.1;
        private double _nextClickTime = 0.2;
        private double _timeout = 3;


        public int Clicks { get => _clicks; set => SetProperty(ref _clicks, value); }
        public double ClickGap { get => _clickGap; set => SetProperty(ref _clickGap, value); }
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }
        public double NextClickTime { get => _nextClickTime; set => SetProperty(ref _nextClickTime, value); }


        public ClickImagesStep()
        {
            Type = StepType.ClickImages;
        }
        public ClickImagesStep(string name)
        {
            Name = name;
            Type = StepType.ClickImages;
        }
        public ClickImagesStep(string name, string description)
        {
            Name = name;
            Description = description;
            Type = StepType.ClickImages;
        }

        public override AutomationStepBase Clone()
        {
            var res = new ClickImagesStep(Name, Description)
            {
                Type = Type,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsTrue = IsTrue,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                OneByOne = OneByOne,
                Images = new(Images?.Select(img => (ImageRecognition)img.Clone()).ToList()),
                Clicks = Clicks,
                ClickGap = ClickGap,
                WaitTime = WaitTime,
                Timeout = Timeout
            };
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            bool res = false;
            double lastSimilarity = -1;
            Models.Point lastClickPos = null;

            foreach (var image in Images)
            {
                var sourceImage = (image.CroppedImg ?? image.ImgSrc) ?? throw new Exception("No image available for clicking.");
                var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
                try
                {
                    // 等待时间按秒换算毫秒：必须 (int)(WaitTime * 1000)，否则小数秒会被截断成 0
                    // 没有点击点时 Autogui 会按匹配区域中心点击，避免“匹配成功却不点”
                    var rect = await Task.Run(async () =>
                    {
                        await Task.Delay((int)(WaitTime * 1000), cancellationToken);
                        return Autogui.ClickImageOnScreenEx(img, Autogui.Position.LeftTop, image.ClickPoints, image.SimilarityThreshold, Clicks, ClickGap, NextClickTime, 0, Timeout);
                    });
                    res = !rect.IsEmpty;
                    lastSimilarity = rect.Similarity;
                    lastClickPos = rect.IsEmpty ? null : rect.Center;
                }
                finally
                {
                    // 无论成功、失败还是取消都释放 GDI+ 位图
                    img.Dispose();
                }
            }
            IsTrue = res;
            IsError = false;
            ErrorType = StepErrorType.None;

            // 调试：标示点击位置
            if (IsTrue && lastClickPos != null && Utils.SingletonLocator.Settings.Step.ShowClickPositionOnRun)
            {
                double dpiX = Services.OCRService.CachedDpiX;
                double dpiY = Services.OCRService.CachedDpiY;
                const double markerSize = 20;
                var clickRegion = new System.Windows.Rect(
                    lastClickPos.X / dpiX - markerSize / 2, lastClickPos.Y / dpiY - markerSize / 2,
                    markerSize, markerSize);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    Views.WindowRegionOverlay.ShowRegion(clickRegion, Utils.SingletonLocator.Settings.Step.ClickPositionOverlay.Color, Utils.SingletonLocator.Settings.Step.ClickPositionOverlay.Duration));
            }

            // 填充执行结果
            LastResult = new StepExecutionResult
            {
                IsTrue = IsTrue,
                Similarity = lastSimilarity,
                ClickPosition = lastClickPos,
            };

            return IsTrue;
        }
    }

    public class FindImagesStep : ImagesRecognitionBase
    {
        private double _gaptime = 0.1;
        public double GapTime { get => _gaptime; set => SetProperty(ref _gaptime, value); }


        private double _timeout = 3;
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }

        public FindImagesStep()
        {
            Type = StepType.FindImages;
        }
        public FindImagesStep(string name)
        {
            Type = StepType.FindImages;
            Name = name;
        }
        public FindImagesStep(string name, string description)
        {
            Type = StepType.FindImages;
            Name = name;
            Description = description;
        }
        public override AutomationStepBase Clone()
        {
            var res = new FindImagesStep(Name, Description)
            {
                Type = Type,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsTrue = IsTrue,
                IsSave = IsSave,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                IsNeed = IsNeed,
                OneByOne = OneByOne,
                Images = new(Images?.Select(img => (ImageRecognition)img.Clone()).ToList()),
                WaitTime = WaitTime,
                GapTime = GapTime,
                Timeout = Timeout
            };
            return res;
        }
        public override async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            List<AutoRect> res;
            // 每张图片的模板位图都必须在结束时释放（原来只在成功路径返回，异常/超时后会泄漏）
            List<AutoguiImage> autoguiImages = Images.Select(x => new AutoguiImage()
            {
                Bitmap = Utils.Autogui.ConvertImageSourceToBitmap(x.CroppedImg ?? x.ImgSrc ?? throw new Exception(LanguageService.GetLocalizedString("No_img_Warning"))),
                Position = Autogui.Position.LeftTop,
                Threshold = x.SimilarityThreshold
            }).ToList();
            try
            {
                res = await Task.Run(async () =>
                {
                    // 等待时间按秒换算毫秒：必须 (int)(WaitTime * 1000)，否则小数秒会被截断成 0
                    await Task.Delay((int)(WaitTime * 1000), cancellationToken);
                    return Autogui.FindImagesOnScreen(autoguiImages, GapTime, Timeout);
                });
            }
            finally
            {
                foreach (var autoguiImage in autoguiImages)
                    autoguiImage.Bitmap?.Dispose();
            }

            // 未匹配时返回的是空结果占位（不再抛异常），保持与模板顺序一致
            var first = res != null && res.Count > 0 ? res[0] : new AutoRect();
            IsTrue = !first.IsEmpty;
            IsError = false;
            ErrorType = StepErrorType.None;

            // 填充执行结果
            LastResult = new StepExecutionResult
            {
                IsTrue = IsTrue,
                Similarity = first.Similarity,
                ClickPosition = first.IsEmpty ? null : first.Center,
            };

            return IsTrue;
        }
    }

}
