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

namespace ShaoLu.Viewmodels.AutomationStep
{
    public class ImageRecognition : ImageRecognitionBase
    {
        public override AutomationStepBase Clone()
        {
            throw new NotImplementedException();
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
        public int Clicks { get => _clicks; set => SetProperty(ref _clicks, value); }


        private double _clickGap = 0.1;
        public double ClickGap { get => _clickGap; set => SetProperty(ref _clickGap, value); }


        private double _waitTime = 0;
        public double WaitTime { get => _waitTime; set => SetProperty(ref _waitTime, value); }


        private double _timeout = 3;
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }


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
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                IsTrue = IsTrue,
                OneByOne = OneByOne,
                Images = Images,
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
            foreach (var image in Images)
            {
                var sourceImage = (image.CroppedImg ?? image.ImgSrc) ?? throw new Exception("No image available for clicking.");
                var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
                res = await Task.Run(() =>
                {
                    return Autogui.ClickImageOnScreen(img, Autogui.Position.LeftTop, image.Offset, image.SimilarityThreshold, Clicks, ClickGap, WaitTime, Timeout);
                });
            }
            IsTrue = res;
            IsError = false;

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
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                IsTrue = IsTrue,
                IsSave = IsSave,
                OneByOne = OneByOne,
                Images = Images,
                GapTime = GapTime,
                Timeout = Timeout
            };
            return res;
        }
        public override async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            List<AutoguiModel.Apoint> res = [];
            List<AutoguiImage> autoguiImages = Images.Select(x => new AutoguiImage()
            {
                Bitmap = Utils.Autogui.ConvertImageSourceToBitmap(x.CroppedImg ?? x.ImgSrc ?? throw new Exception(LanguageService.GetLocalizedString("No_img_Warning"))),
                Position = Autogui.Position.LeftTop,
                PositionOffset = x.Offset,
                Threshold = x.SimilarityThreshold
            }).ToList();
            res = await Task.Run(() => { return Autogui.FindImagesOnScreen(autoguiImages, GapTime, Timeout); });
            IsTrue = !res[0].IsEmpty;
            IsError = false;
            return IsTrue;
        }
    }

}
