using ImageMagick;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ImageTool
{
    public class Image : IDisposable,INotifyPropertyChanged
    {
        public string Name { get; }

        public String Path { get; }

        public Bitmap Bitmap { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        private Dictionary<string, string> _exif;
        public Dictionary<string, string> Exif 
        { get { return _exif; }
            private set 
            {
                _exif = value;
                OnPropertyChanged("Exif");
            } 
        }

        public Boolean Initialized { get; private set; }

        public Boolean IsAnimation
        {
            get
            {
                if (Bitmap == null) return false;

                try
                {
                    return Bitmap.RawFormat.ToString().ToUpper().Equals("GIF");
                }
                catch
                {
                    return false;
                }
            }
        }

        public Image(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
        }

        public Task InitializeAsync()
        {
            if (!Initialized)
            {
                return Task.Run(() =>
                {
                    Bitmap = ImageDecoder.GetImage(Path, out Dictionary<string, string> exif);
                    Exif = exif;

                    if (Bitmap != null)
                    {
                        Width = Bitmap.Width;
                        Height = Bitmap.Height;
                    }
                });
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        public BitmapSource GetBitmapSource()
        {
            if (Bitmap == null)
            {
                Bitmap = ImageDecoder.GetImage(Path, out Dictionary<string, string> exif);
                Exif = exif;
            }

            var image = ImageDecoder.GetBitmapSource(Bitmap);

            image.Freeze();

            return image;
        }

        public BitmapSource GetThumbSource()
        {
            return ImageDecoder.GetThumb(Path, 60, 200);
        }

        #region Animation

        public delegate void FrameUpdatedEventHandler();

        public event EventHandler<BitmapSource> SourceAnimate;
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public bool isAnimated = false;

        public void StartAnimate()
        {
            isAnimated = true;

            if (ImageAnimator.CanAnimate(Bitmap))
            {
                ImageAnimator.Animate(Bitmap, OnFrameChanged);
            }
        }

        public void StopAnimate()
        {
            isAnimated = false;

            ImageAnimator.StopAnimate(Bitmap, OnFrameChanged);
        }

        public void OnFrameChanged(object sender, EventArgs e)
        {
            try
            {
                ImageAnimator.UpdateFrames(Bitmap);

                SourceAnimate?.Invoke(this, GetBitmapSource());
            }
            catch { }
        }

        #endregion

        public void Dispose()
        {
            if (isAnimated)
            {
                StopAnimate();
            }

            if (SourceAnimate != null)
            {
                foreach (Delegate del in SourceAnimate.GetInvocationList())
                {
                    SourceAnimate -= del as EventHandler<BitmapSource>;
                }
            }

            Bitmap?.Dispose();
            Bitmap = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
