using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ImageTool.Extensions;
using ImageTool.Utils;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using ImageTool.Helpers;
using ImageTool.Services;
using System.Windows.Ink;
using System.Drawing.Imaging;
using System.Threading;

namespace ImageTool
{
    public partial class ImageEditor : UserControl
    {
        private InkCanvasEditingMode _lastPenMode = InkCanvasEditingMode.Ink;

        public Window MainWin = null;


        public CropService CropService { get; private set; }

        #region DependencyProperty

        #region 图片源
        public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(nameof(ImageSource), typeof(object), typeof(ImageEditor), new PropertyMetadata(new PropertyChangedCallback((sender, e) =>
        {

            if (sender is ImageEditor imageEditor && e.NewValue is string localPath)
            {
                imageEditor.OpenImage(localPath);
            }

        })));

        public object ImageSource
        {
            get
            {
                return (object)GetValue(ImageSourceProperty);
            }
            set
            {
                SetValue(ImageSourceProperty, value);
            }
        }

        #endregion


        #region 启用快捷键
        public static readonly DependencyProperty IsKeyInputEnabledProperty = DependencyProperty.Register(nameof(IsKeyInputEnabled), typeof(bool), typeof(ImageEditor), new PropertyMetadata(false));
      

        public bool IsKeyInputEnabled
        {
            get
            {
                return (bool)GetValue(IsKeyInputEnabledProperty);
            }
            set
            {
                SetValue(IsKeyInputEnabledProperty, value);
            }
        }
        #endregion


        #region 是否加载了文件夹
        public bool IsLoadFolder
        {
            get { return (bool)GetValue(IsLoadFolderProperty); }
            set { SetValue(IsLoadFolderProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsLoadFolder.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsLoadFolderProperty =
            DependencyProperty.Register("IsLoadFolder", typeof(bool), typeof(ImageEditor), new PropertyMetadata(true));
        #endregion


        #region 显示裁剪框
        public bool ShowCutter
        {
            get { return (bool)GetValue(ShowCutterProperty); }
            set { SetValue(ShowCutterProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ShowCutter.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ShowCutterProperty =
            DependencyProperty.Register("ShowCutter", typeof(bool), typeof(ImageEditor), new PropertyMetadata(false));
        #endregion


        #region 是否显示【打开文件】按钮
        public bool ShowOpen
        {
            get { return (bool)GetValue(ShowOpenProperty); }
            set { SetValue(ShowOpenProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ShowOpen.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ShowOpenProperty =
            DependencyProperty.Register("ShowOpen", typeof(bool), typeof(ImageEditor), new PropertyMetadata(true));
        #endregion


        #region 是否显示【保存】按钮
        public bool ShowSave
        {
            get { return (bool)GetValue(ShowSaveProperty); }
            set { SetValue(ShowSaveProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ShowSave.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ShowSaveProperty =
            DependencyProperty.Register("ShowSave", typeof(bool), typeof(ImageEditor), new PropertyMetadata(true));
        #endregion


        #region 画笔颜色
        public Color PenColor
        {
            get { return (Color)GetValue(PenColorProperty); }
            set { SetValue(PenColorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for PenColor.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PenColorProperty =
            DependencyProperty.Register("PenColor", typeof(Color), typeof(ImageEditor), new PropertyMetadata(Colors.Black));
        #endregion


        #region 保存时是否覆盖现有文件

        public bool IsCoverSave
        {
            get { return (bool)GetValue(IsCoverSaveProperty); }
            set { SetValue(IsCoverSaveProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsCoverSave.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsCoverSaveProperty =
            DependencyProperty.Register("IsCoverSave", typeof(bool), typeof(ImageEditor), new PropertyMetadata(true));

        #endregion


        #endregion


        public ImageEditor()
        {
            this.DataContext = new ImageEditorViewModel();

            InitializeComponent();

            InitializeImageViewInfoPanelState();

            InitializeTransformGroup();

            this.Loaded += (s, e) =>
            {
                RenderOptions.SetBitmapScalingMode(ImageViewer, BitmapScalingMode.HighQuality);

                this.AllowDrop = true;
                this.Drop += ImageView_Drop;
                this.DragEnter += ImageView_DragEnter;
                this.PreviewKeyDown += ImageView_PreviewKeyDown;
                this.PreviewMouseRightButtonDown += ImageView_PreviewMouseRightButtonDown;
                this.MouseWheel += ImageView_MouseWheel;

                imgCurrent.ImageFailed += ImageViewer_ImageFailed;
                ImageViewer.MouseLeftButtonDown += ImageViewer_MouseLeftButtonDown;
                ImageViewer.MouseLeftButtonUp += ImageViewer_MouseLeftButtonUp;
                ImageViewer.MouseMove += ImageViewer_MouseMove;

                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                //CropService = new CropService(ImageViewContainer);
                //CropService.Adorner.Visibility = Visibility.Collapsed;                 
            };

            this.Unloaded += (s, e) =>
            {
                this.AllowDrop = false;
                this.Drop -= ImageView_Drop;
                this.DragEnter -= ImageView_DragEnter;
                this.PreviewKeyDown -= ImageView_PreviewKeyDown;
                this.PreviewMouseRightButtonDown -= ImageView_PreviewMouseRightButtonDown;
                this.ImageViewer.MouseWheel -= ImageView_MouseWheel;

                imgCurrent.ImageFailed -= ImageViewer_ImageFailed;
                ImageViewer.MouseLeftButtonDown -= ImageViewer_MouseLeftButtonDown;
                ImageViewer.MouseLeftButtonUp -= ImageViewer_MouseLeftButtonUp;
                ImageViewer.MouseMove -= ImageViewer_MouseMove;

                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            };
        }

        ImageEditorViewModel ViewModel => this.DataContext as ImageEditorViewModel;

        /// <summary>
        /// 属性变更事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CurrentImage")
            {
                if (ViewModel.CurrentImage == null) return;

                this.ShowCutter = false;
                this.CropService = null;

                await ViewModel.CurrentImage.InitializeAsync();
                //txtScale.Text = "100%";

                if (ViewModel.CurrentImage.IsAnimation)
                {
                    ViewModel.CurrentImage.SourceAnimate += (sender, e) =>
                    {
                        imgCurrent.Source = e;
                        ImageViewer.InvalidateVisual();
                    };

                    ViewModel.CurrentImage.StartAnimate();
                  
                }

                if (ViewModel.CurrentImage.Height > 300)
                    this.Height = ViewModel.CurrentImage.Height+100;
                //else
                //    this.Height = 300;

                ResetImageView();

               await Task.Run(() => 
               {
                   Thread.Sleep(210);
                   Dispatcher.Invoke(() => { SetScale(100); });
               });
               // var size = GetActualSize(imgCurrent);

                //ReloadImage();
            }
        }


        #region Load

        public void OpenImage(string path)
        {
            if (ViewModel.IsImageLoading)
            {
                return;
            }

            if (!String.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                ViewModel.IsImageLoadFailed = false;

                ViewModel.IsImageLoading = true;

                try
                {
                    if (!File.Exists(path))
                        throw new FileNotFoundException(path);

                    if ((ViewModel.Folder == null || (ViewModel.Folder != null && !ViewModel.Folder.Any(t => t.Path == path))) && IsLoadFolder)
                    {
                        ViewModel.Folder = new Folder(Path.GetDirectoryName(path));
                    }
                    else
                    {
                        ViewModel.Folder = new Folder();
                        ViewModel.Folder.Add(new Image(path));
                    }

                    ViewModel.CurrentImage = ViewModel.Folder.Find(t => t.Path == path);
                }
                catch (Exception ex)
                {
                    ViewModel.CurrentImage = null;

                    ViewModel.IsImageLoadFailed = true;

#if DEBUG
                    Trace.WriteLine("ImageViewer : Load Image Failed...", ex.Message);
#endif
                }
                finally
                {
                    this.ChangeArrowButtonEnabled();

                    ViewModel.IsImageLoading = false;
                }
            }
            else
            {
                return;
            }
        }

        public void ResetImageView(bool quickReset = false,double scale=70.0)
        {
            if (ViewModel.CurrentImage == null)
                return;

            double width = ViewModel.CurrentImage.Width;
            double height = ViewModel.CurrentImage.Height;

            while (width > ImageViewContainer.ActualWidth * scale / 100)
            {
                width = width * scale / 100;
            }

            while (height > ImageViewContainer.ActualHeight * scale / 100)
            {
                height = height * scale / 100;
            }

            ImageViewer.Width = width;
            ImageViewer.Height = height;

            // reset zoom
            var st = GetScaleTransform();
          
            AnimateScaleTransform(st, 1.0, ZoomAndTranslateAnimationDurationInMs, false, true);

            // reset pan
            var tt = GetTranslateTransform();
            AnimateTranslateTransform(tt, 0.0, 0.0, quickReset ? QuickAnimationDurationInMs : ZoomAndTranslateAnimationDurationInMs);

            var rt = GetRotateTransform();
            AnimateRotateTransform(rt, 0, QuickAnimationDurationInMs);


        }

        private void ImageViewer_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ViewModel.CurrentImage = null;

            ViewModel.IsImageLoadFailed = true;

#if DEBUG
            Trace.WriteLine("ImageViewer : Load Image Failed...", e.ErrorException.Message);
#endif
        }

        #endregion

        #region Drop

        private void ImageView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                OpenImage(files[0]);
            }
        }

        private void ImageView_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Link;
            else e.Effects = DragDropEffects.None;
        }

        #endregion

        #region Arrow

        private void ChangeArrowButtonEnabled()
        {
            if (ViewModel.CurrentImage != null && ViewModel.Folder?.Count > 1)
            {
                this.Button_Prev.Visibility = Visibility.Visible;

                this.Button_Next.Visibility = Visibility.Visible;

                this.Button_Prev_Panel.Visibility = Visibility.Visible;

                this.Button_Next_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                this.Button_Prev.Visibility = Visibility.Collapsed;

                this.Button_Next.Visibility = Visibility.Collapsed;

                this.Button_Prev_Panel.Visibility = Visibility.Collapsed;

                this.Button_Next_Panel.Visibility = Visibility.Collapsed;
            }
        }

        private void Button_Prev_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            if (ViewModel.Folder?.Count > 0)
            {
                var index = ViewModel.Folder.IndexOf(ViewModel.CurrentImage);

                if (index > 0)
                {
                    ViewModel.CurrentImage = ViewModel.Folder[index - 1];
                }
                else
                {
                    ViewModel.CurrentImage = ViewModel.Folder.Last();
                }
            }
        }

        private void Button_Next_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            if (ViewModel.Folder?.Count > 0)
            {
                var index = ViewModel.Folder.IndexOf(ViewModel.CurrentImage);

                if (index < ViewModel.Folder.Count - 1)
                {
                    ViewModel.CurrentImage = ViewModel.Folder[index + 1];
                }
                else
                {
                    ViewModel.CurrentImage = ViewModel.Folder.First();
                }
            }
        }


        #endregion

        #region TransformGroup

        public void InitializeTransformGroup()
        {
            TransformGroup transformGroup = new TransformGroup();

            transformGroup.Children.Add(new TranslateTransform());
            transformGroup.Children.Add(new ScaleTransform());
            transformGroup.Children.Add(new RotateTransform());

            ImageViewer.RenderTransform = transformGroup;
            ImageViewer.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private TranslateTransform GetTranslateTransform()
        {
            return (TranslateTransform)((TransformGroup)ImageViewer.RenderTransform).Children.First(tr => tr is TranslateTransform);
        }

        private ScaleTransform GetScaleTransform()
        {
            ScaleTransform scaleTransform = (ScaleTransform)((TransformGroup)ImageViewer.RenderTransform).Children.First(tr => tr is ScaleTransform);

            return scaleTransform;
        }

        private RotateTransform GetRotateTransform()
        {
            var rt = (RotateTransform)((TransformGroup)ImageViewer.RenderTransform).Children.First(tr => tr is RotateTransform);

            if (rt.Angle == -360)
            {
                rt.Angle = 0;
            }

            return rt;
        }

        private void Button_Reload_Click(object sender, RoutedEventArgs e)
        {
            ResetImageView();
        }

        private void ReloadImage() 
        {
            if (imageBox.ActualWidth > ViewModel.CurrentImage.Width)
            {
                SetScale(100);
            }
            else
            {
                ResetImageView();
            }
        }

        private void ImageView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isImageViewInfoPanelOpen)
            {
                VisualStateManager.GoToElementState(this, "ImageViewInfoPanelClosed", true);
            }
            else
            {
                ResetImageView();
            }
        }

        #endregion

        #region Move(TranslateTransform)

        private Point dargPosition;
        private Point dropPosition;
        private bool isMoving;

        private bool translateAvailable = true;

        private void AnimateTranslateTransform(TranslateTransform tt, double toX, double toY, int durationInMs)
        {
            if (translateAvailable == true)
            {
                translateAvailable = false;

                var animationX = new DoubleAnimation(toX, TimeSpan.FromMilliseconds(durationInMs))
                {
                    EasingFunction = new QuarticEase() { EasingMode = EasingMode.EaseOut }
                };

                var animationY = new DoubleAnimation(toY, TimeSpan.FromMilliseconds(durationInMs))
                {
                    EasingFunction = new QuarticEase() { EasingMode = EasingMode.EaseOut }
                };

                void AnimationCompleted(object s, EventArgs e)
                {
                    //animationX.Completed -= AnimationCompleted;
                    translateAvailable = true;
                }

                animationX.Completed += AnimationCompleted;

                tt.BeginAnimation(TranslateTransform.XProperty, animationX, HandoffBehavior.Compose);

                tt.BeginAnimation(TranslateTransform.YProperty, animationY, HandoffBehavior.Compose);
            }
        }

        private void ImageViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.CurrentImage == null) return;

            if (sender is System.Windows.Controls.Grid img)
            {
                if (e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed)
                {
                    dargPosition = e.GetPosition(img);

                    isMoving = true;
                }
                else if (e.ClickCount == 2)
                {
                    Point relative = e.GetPosition(ImageViewer);
                    Zoom(relative.X, relative.Y, 2);
                }
            }


        }

        private void ImageViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isMoving = false;
        }

        private void ImageViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (ViewModel.CurrentImage == null) return;

            if (sender is System.Windows.Controls.Grid img && isMoving && e.LeftButton == MouseButtonState.Pressed)
            {
                dropPosition = e.GetPosition(img);

                var tt = GetTranslateTransform();

                var x = tt.X + dropPosition.X - dargPosition.X;
                var y = tt.Y + dropPosition.Y - dargPosition.Y;

                AnimateTranslateTransform(tt, x, y, 0);
            }
        }

        #endregion

        #region Rotate(RotateTransform)

        private static void AnimateRotateTransform(RotateTransform rt, double to, int durationInMs)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationInMs))
            {
                EasingFunction = new QuarticEase() { EasingMode = EasingMode.EaseOut }
            };
            rt.BeginAnimation(RotateTransform.AngleProperty, animation);
        }

        private void Button_RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            var rt = GetRotateTransform();

            var to = rt.Angle;

            to -= 5;

            AnimateRotateTransform(rt, to, 400);
        }

        private void Button_RotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            var rt = GetRotateTransform();

            var to = rt.Angle;

            to += 5;

            AnimateRotateTransform(rt, to, 400);
        }

        private void Button_RotateLeftVariant_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            var rt = GetRotateTransform();

            var to = rt.Angle;

            to -= 90;

            AnimateRotateTransform(rt, to, 400);
        }

        private void Button_RotateRightVariant_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            var rt = GetRotateTransform();

            var to = rt.Angle;

            to += 90;

            AnimateRotateTransform(rt, to, 400);
        }


        private void imgCurrent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            GetPrecent();
        }

        /// <summary>
        /// 获取真实尺寸百分比
        /// </summary>
        /// <returns></returns>
        private double GetPrecent() 
        {
            Size actualSize = GetActualSize(imgCurrent);
            double scale = 0;

            if (ViewModel.CurrentImage.Width<ViewModel.CurrentImage.Height)
            {
                scale=actualSize.Height/ViewModel.CurrentImage.Height;
            }
            else 
            {
                scale=actualSize.Width/ViewModel.CurrentImage.Width;
            }


            txtScale.Text =NumberUtils.GetPercentages(scale);

            return scale;
        }

        public static Size GetActualSize(FrameworkElement control)
        {
            Size startSize = new Size(control.ActualWidth, control.ActualHeight);

            // go up parent tree until reaching root
            var parent = LogicalTreeHelper.GetParent(control);
            while (parent != null && parent as FrameworkElement != null && parent.GetType() != typeof(Window))
            {
                // try to find a scale transform
                FrameworkElement fp = parent as FrameworkElement;
                ScaleTransform scale = FindScaleTransform(fp.RenderTransform);
                if (scale != null)
                {
                    startSize.Width *= scale.ScaleX;
                    startSize.Height *= scale.ScaleY;
                }
                parent = LogicalTreeHelper.GetParent(parent);
            }
            // return new size
            return startSize;
        }

        public static ScaleTransform FindScaleTransform(Transform hayStack)
        {
            if (hayStack is ScaleTransform)
            {
                return (ScaleTransform)hayStack;
            }
            if (hayStack is TransformGroup)
            {
                TransformGroup group = hayStack as TransformGroup;
                foreach (var child in group.Children)
                {
                    if (child is ScaleTransform)
                    {
                        return (ScaleTransform)child;
                    }
                }
            }
            return null;
        }


        #endregion

        #region Zoom(ScaleTransform)

        private const double MaxZoomScale = 10.0;

        private const double MinZoomScale = 0.1;

        private const int ZoomAndTranslateAnimationDurationInMs = 200;

        private const int QuickAnimationDurationInMs = 10;

        private const double ZoomFactor = 1.3;

        private  void AnimateScaleTransform(ScaleTransform st, double to, int durationInMs, bool stop = false, bool isCentered = false)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationInMs))
            {
                EasingFunction = new QuarticEase() { EasingMode = EasingMode.EaseOut },
                FillBehavior = stop ? FillBehavior.Stop : FillBehavior.HoldEnd
            };

            void AnimationCompleted(object sender, EventArgs e)
            {
                animation.Completed -= AnimationCompleted;

                if (isCentered)
                {
                    st.CenterX = 0;
                    st.CenterY = 0;
                }

                GetPrecent();
            }

            animation.Completed += AnimationCompleted;

            st.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private void Zoom(double relativeX, double relativeY, double zoomScale)
        {
            var st = GetScaleTransform();

            if (st.ScaleX >= zoomScale)
            {
                return;
            }

            st.CenterX = -(ImageViewer.RenderSize.Width / zoomScale) - relativeX;
            st.CenterY = -(ImageViewer.RenderSize.Height / zoomScale) - relativeY;

            var tt = GetTranslateTransform();

            double abosuluteX = relativeX * st.ScaleX + tt.X;
            double abosuluteY = relativeY * st.ScaleY + tt.Y;

            var newScale = st.ScaleX * zoomScale;

            AnimateScaleTransform(st, newScale, ZoomAndTranslateAnimationDurationInMs);
            AnimateTranslateTransform(tt, abosuluteX - relativeX * newScale, abosuluteY - relativeY * newScale, ZoomAndTranslateAnimationDurationInMs);
        }

        private void Button_ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            var st = GetScaleTransform();

            var newScale = st.ScaleX *= ZoomFactor;

            if (newScale >= MaxZoomScale)
            {
                newScale = MaxZoomScale;
            }
            
            AnimateScaleTransform(st, newScale, ZoomAndTranslateAnimationDurationInMs);
        }

        private void Button_ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            var st = GetScaleTransform();

            var newScale = st.ScaleX /= ZoomFactor;

            if (newScale <= MinZoomScale)
            {
                newScale = MinZoomScale;
            }
           
            AnimateScaleTransform(st, newScale, ZoomAndTranslateAnimationDurationInMs);
        }

        private void btnOrigianlSize_Click(object sender, RoutedEventArgs e)
        {
            SetScale(100);
        }

        private void ScaleInputChanged() 
        {
            double scale = 100;

            var str = txtScale.Text.Replace("%", "");

            if (!double.TryParse(str, out scale))
            {
                scale=100;
                txtScale.Text ="100%";
            }

            SetScale(scale);
        }

        /// <summary>
        /// 设置图像显示比例
        /// </summary>
        /// <param name="precent">需要切换到真实尺寸的百分比</param>
        private void SetScale(double precent) 
        {
            var st = GetScaleTransform();

            //在WEPF中控件Transform的比例
            var scaleX = st.ScaleX;

            //真实尺寸百分比
            var actualPrecent = GetPrecent()*100;

            //scaleX/actualPrecent = newScaleX/precent
            var newScaleX = scaleX*precent/actualPrecent;


            if (newScaleX<MinZoomScale) 
            {
                newScaleX=MinZoomScale;
            }

            if (newScaleX>MaxZoomScale) 
            {
                newScaleX=MaxZoomScale;
            }

            AnimateScaleTransform(st, newScaleX, ZoomAndTranslateAnimationDurationInMs);
            GetPrecent();
        }

        private void txtScale_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key== Key.Enter)
                ScaleInputChanged();
        }

        private void txtScale_LostFocus(object sender, RoutedEventArgs e)
        {
            ScaleInputChanged();
        }

        private void ImageView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ViewModel.CurrentImage == null)
                return;

            bool incrementZoom = e.Delta > 0;

            var st = GetScaleTransform();

            double newScale;

            if (incrementZoom)
            {
                newScale = st.ScaleX *= ZoomFactor;

                if (newScale >= MaxZoomScale)
                {
                    newScale = MaxZoomScale;
                }
            }
            else
            {
                newScale = st.ScaleX /= ZoomFactor;

                if (newScale <= MinZoomScale)
                {
                    newScale = MinZoomScale;
                }
            }

            AnimateScaleTransform(st, newScale, ZoomAndTranslateAnimationDurationInMs);
        }

        #endregion

        #region InfoPanel

        private bool isImageViewInfoPanelOpen = false;

        private void InitializeImageViewInfoPanelState()
        {
            VisualStateManager.GoToElementState(this, "ImageViewInfoPanelClosed", true);
        }

        private void Button_Info_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentImage != null)
            {
                isImageViewInfoPanelOpen = !isImageViewInfoPanelOpen;

                VisualStateManager.GoToElementState(this, isImageViewInfoPanelOpen ? "ImageViewInfoPanelOpen" : "ImageViewInfoPanelClosed", true);
            }
            else
            {
                VisualStateManager.GoToElementState(this, "ImageViewInfoPanelClosed", true);
            }
        }

        #endregion


        #region Shortcuts

        public void PressShortcutKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left)
            {
                Button_Prev_Click(null, null);
            }
            else if (e.Key == Key.Right)
            {
                Button_Next_Click(null, null);
            }
            else if (e.Key == Key.Up || e.Key == Key.OemPlus)
            {
                Button_ZoomIn_Click(null, null);
            }
            else if (e.Key == Key.Down || e.Key == Key.OemMinus)
            {
                Button_ZoomOut_Click(null, null);
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.OemPeriod)
            {
                Button_RotateRight_Click(null, null);
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
            {
                Button_RotateLeft_Click(null, null);
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                Save();
            }
            else if (e.Key == Key.OemPeriod)
            {
                Button_RotateRightVariant_Click(null, null);
            }
            else if (e.Key == Key.OemComma)
            {
                Button_RotateLeftVariant_Click(null, null);
            }
            else if (e.Key == Key.Back)
            {
                Button_Reload_Click(null, null);
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.Z && cboPenMode.IsChecked==true)
            {
                inkCanvas.Undo();
            }
        }

        private void ImageView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            if (IsKeyInputEnabled)
            {
                FocusManager.SetIsFocusScope(this, true);
                FocusManager.SetFocusedElement(this, this);

                PressShortcutKey(sender, e);
            }
        }

        #endregion


        private void ImageViewGallery_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = ImageViewGallery.GetVisualChild<ScrollViewer>();

            if (e.Delta > 0)
            {
                scrollViewer.LineLeft();
                scrollViewer.LineLeft();
            }
            else
            {
                scrollViewer.LineRight();
                scrollViewer.LineRight();
            }

            e.Handled = true;
        }


        private void Button_Cut_Click(object sender, RoutedEventArgs e)
        {
            this.ShowCutter = !this.ShowCutter;


            if (CropService==null) 
            {
                var size = GetActualSize(imgCurrent);
                CropService=new CropService(ImageViewContainer, size.Width,size.Height);
            }

            if (this.ShowCutter) { CropService.Adorner.Visibility = Visibility.Visible; }
            else CropService.Adorner.Visibility = Visibility.Collapsed;
        }


        private void OpenImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter =
                "All supported (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.ico;*.tiff;*.wmf)|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.ico;*.tiff;*.wmf|" +
                "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
                "Portable Network Graphic (*.png)|*.png|" +
                "Graphics Interchange Format (*.gif)|*.gif|" +
                "Icon (*.ico)|*.ico|" +
                "Other (*.tiff;*.wmf)|*.tiff;*.wmf|" +
                "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() is true)
                OpenImage(dialog.FileName);
        }

        //保存裁剪区内容
        public string SaveCropRect(string savePath = "")
        {
            var cropArea = CropService.GetCroppedArea();

            System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle((int)cropArea.CroppedRectAbsolute.X,
                (int)cropArea.CroppedRectAbsolute.Y, (int)cropArea.CroppedRectAbsolute.Width,
                (int)cropArea.CroppedRectAbsolute.Height);

            if (string.IsNullOrEmpty(savePath))
                savePath = ViewModel.CurrentImage.Path;

            if (!IsCoverSave) 
            {
                FileInfo fi=new FileInfo(savePath);
                Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog()
                {
                    Filter="图片文件(*.jpg,*.png,*.bmp)|*.jpg;*.png;*.bmp",
                    InitialDirectory=fi.DirectoryName                    
                };

                if (!dlg.ShowDialog().GetValueOrDefault()) 
                {
                    return "";
                }

                savePath=dlg.FileName;
            }

            System.Drawing.Bitmap target = new System.Drawing.Bitmap(cropRect.Width, cropRect.Height);

            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(target))
            {
                var sourceBitmap = ImageWork.ControllerToBitmap(ImageViewContainer);

                g.Clear(System.Drawing.Color.White);

                g.DrawImage(sourceBitmap, new System.Drawing.Rectangle(0, 0, target.Width, target.Height),
                    cropRect, System.Drawing.GraphicsUnit.Pixel);

                //this.CutImage.Source= ImageWork.BitMapSourceFromBitmap(target);
            }

            if (File.Exists(savePath)) 
            {
                File.Delete(savePath);
            }

            ImageFormat format = ImageFormat.Jpeg;
            var extName= GetFileExtNonePoint(savePath).ToLower();

            if (string.IsNullOrEmpty(extName)) extName="jpg";

            switch (extName) 
            {
                case "png":
                    format = ImageFormat.Png;
                    break;
                case "bmp":
                    format= ImageFormat.Bmp;
                    break;
            }

            target.Save(savePath,format);

            return savePath;
        }

        /// <summary>
        /// 返回文件路径的后缀（无"."）
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetFileExtNonePoint(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            int index = path.LastIndexOf(".", StringComparison.Ordinal);
            if (index >= 0)
            {
                return path.Substring(path.LastIndexOf('.') + 1);
            }
            return path;
        }

        public string Save(string savePath="") 
        {
            if (this.ShowCutter)
            {
                savePath =SaveCropRect();
                return savePath;
            }

            savePath = ViewModel.CurrentImage.Path;
            return savePath;
        }


        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        #region InkCanvas
        private void cboPenMode_Checked(object sender, RoutedEventArgs e)
        {
            inkCanvas.EditingMode=_lastPenMode;
        }

        private void cboPenMode_Unchecked(object sender, RoutedEventArgs e)
        {
            inkCanvas.EditingMode=InkCanvasEditingMode.None;
        }

        private void rdoPenMode_Click(object sender, RoutedEventArgs e)
        {
            RadioButton rad = sender as RadioButton;
            this.inkCanvas.EditingMode = (InkCanvasEditingMode)rad.Tag;
            _lastPenMode=this.inkCanvas.EditingMode;
           
         
        }

        private void rdoPenSize_Click(object sender, RoutedEventArgs e)
        {
            RadioButton rad = sender as RadioButton;
            DrawingAttributes inkDA = new DrawingAttributes();
            inkDA.Width = rad.FontSize;
            inkDA.Height = rad.FontSize;
            inkDA.Color = this.inkCanvas.DefaultDrawingAttributes.Color;
            inkDA.IsHighlighter = this.inkCanvas.DefaultDrawingAttributes.IsHighlighter;
            this.inkCanvas.DefaultDrawingAttributes = inkDA;
        }

        private void btnPenCut_Click(object sender, RoutedEventArgs e)
        {
            if (this.inkCanvas.GetSelectedStrokes().Count > 0)
                this.inkCanvas.CutSelection();
        }

        private void btnPenCopy_Click(object sender, RoutedEventArgs e)
        {
            if (this.inkCanvas.GetSelectedStrokes().Count > 0)
                this.inkCanvas.CopySelection();
        }

        private void btnPenPaste_Click(object sender, RoutedEventArgs e)
        {
            if (this.inkCanvas.CanPaste())
                this.inkCanvas.Paste();
        }

        private void btnPenDel_Click(object sender, RoutedEventArgs e)
        {
            if (this.inkCanvas.GetSelectedStrokes().Count > 0)
            {
                foreach (Stroke strk in this.inkCanvas.GetSelectedStrokes())
                    this.inkCanvas.Strokes.Remove(strk);
            }
        }

        private void btnPenSelectAll_Click(object sender, RoutedEventArgs e)
        {
            this.inkCanvas.Select(this.inkCanvas.Strokes);
        }

        private void cboPenPressure_Checked(object sender, RoutedEventArgs e)
        {
            this.inkCanvas.DefaultDrawingAttributes.IgnorePressure=false;
        }

        private void cboPenPressure_Unchecked(object sender, RoutedEventArgs e)
        {
            this.inkCanvas.DefaultDrawingAttributes.IgnorePressure=true;
        }

        private void cboPenHighLight_Checked(object sender, RoutedEventArgs e)
        {
            this.inkCanvas.DefaultDrawingAttributes.IsHighlighter=true;
        }

        private void cboPenHighLight_Unchecked(object sender, RoutedEventArgs e)
        {
            this.inkCanvas.DefaultDrawingAttributes.IsHighlighter=false;
        }

        private void colorPicker_SelectedColorChanged(object sender, HandyControl.Data.FunctionEventArgs<Color> e)
        {


        }

        private void colorPicker_Canceled(object sender, EventArgs e)
        {
            cboPenColor.IsChecked = false;
        }

        private void colorPicker_Confirmed(object sender, HandyControl.Data.FunctionEventArgs<Color> e)
        {
            this.inkCanvas.DefaultDrawingAttributes.Color = e.Info;
            cboPenColor.IsChecked = false;
        }




        #endregion

        private void imageEditor_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //ResetImageView();
        }

     
    }
   
  

}
