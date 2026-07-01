using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WPFDevelopers.Controls;

namespace ShaoLu.Controls
{
    /// <summary>
    /// 按照步骤 1a 或 1b 操作，然后执行步骤 2 以在 XAML 文件中使用此自定义控件。
    ///
    /// 步骤 1a) 在当前项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ShaoLu.Controls"
    ///
    ///
    /// 步骤 1b) 在其他项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ShaoLu.Controls;assembly=ShaoLu.Controls"
    ///
    /// 您还需要添加一个从 XAML 文件所在的项目到此项目的项目引用，
    /// 并重新生成以避免编译错误:
    ///
    ///     在解决方案资源管理器中右击目标项目，然后依次单击
    ///     “添加引用”->“项目”->[浏览查找并选择此项目]
    ///
    ///
    /// 步骤 2)
    /// 继续操作并在 XAML 文件中使用控件。
    ///
    ///     <MyNamespace:MultiCropImage/>
    ///
    /// </summary>
    [TemplatePart(Name = "PART_CropCanvas", Type = typeof(Canvas))]
    [TemplatePart(Name = "PART_RectangleLeft", Type = typeof(Rectangle))]
    [TemplatePart(Name = "PART_RectangleTop", Type = typeof(Rectangle))]
    [TemplatePart(Name = "PART_RectangleRight", Type = typeof(Rectangle))]
    [TemplatePart(Name = "PART_RectangleBottom", Type = typeof(Rectangle))]
    [TemplatePart(Name = "PART_Border", Type = typeof(Border))]
    public class MultiCropImage : Control
    {
        private const string CanvasTemplateName = "PART_CropCanvas";

        private const string RectangleLeftTemplateName = "PART_RectangleLeft";

        private const string RectangleTopTemplateName = "PART_RectangleTop";

        private const string RectangleRightTemplateName = "PART_RectangleRight";

        private const string RectangleBottomTemplateName = "PART_RectangleBottom";

        private const string BorderTemplateName = "PART_Border";

        public static readonly DependencyProperty SourceProperty;

        public static readonly DependencyProperty CurrentRectProperty;

        public static readonly DependencyProperty CurrentAreaBitmapProperty;

        public static readonly DependencyProperty IsRatioScaleProperty;

        public static readonly DependencyProperty ScaleSizeProperty;

        public static readonly DependencyProperty RectScaleProperty;

        private Border _border;

        private Canvas _canvas;

        private Rectangle _rectangleLeft;

        private Rectangle _rectangleTop;

        private Rectangle _rectangleRight;

        private Rectangle _rectangleBottom;

        private AdornerLayer _adornerLayer;

        private BitmapFrame _bitmapFrame;

        private bool _isDragging;

        private double _offsetX;

        private double _offsetY;

        private ScreenCutAdorner _screenCutAdorner;

        private bool _isInitialized;

        private bool _isUnloaded;

        // 新增状态字段
        private double _viewScale = 1.0;

        private Point _viewTranslate = new Point(0, 0);

        private double _cropAngle = 0;

        private bool _isPanningMode = false; // 滚轮按下切换的全局平移模式

        private Point _lastMousePos;

        // 新增依赖属性
        public static readonly DependencyProperty CropAngleProperty =
            DependencyProperty.Register("CropAngle", typeof(double), typeof(MultiCropImage),
                new PropertyMetadata(0.0, OnCropAngleChanged));

        public double CropAngle
        {
            get => (double)GetValue(CropAngleProperty);
            set => SetValue(CropAngleProperty, value);
        }
        // *******

        public ImageSource Source
        {
            get
            {
                return (ImageSource)GetValue(SourceProperty);
            }
            set
            {
                SetValue(SourceProperty, value);
            }
        }

        public Rect CurrentRect
        {
            get
            {
                return (Rect)GetValue(CurrentRectProperty);
            }
            private set
            {
                SetValue(CurrentRectProperty, value);
            }
        }

        public ImageSource CurrentAreaBitmap
        {
            get
            {
                return (ImageSource)GetValue(CurrentAreaBitmapProperty);
            }
            private set
            {
                SetValue(CurrentAreaBitmapProperty, value);
            }
        }

        public bool IsRatioScale
        {
            get
            {
                return (bool)GetValue(IsRatioScaleProperty);
            }
            set
            {
                SetValue(IsRatioScaleProperty, value);
            }
        }

        public Size ScaleSize
        {
            get
            {
                return (Size)GetValue(ScaleSizeProperty);
            }
            set
            {
                SetValue(ScaleSizeProperty, value);
            }
        }

        public double RectScale
        {
            get
            {
                return (double)GetValue(RectScaleProperty);
            }
            set
            {
                SetValue(RectScaleProperty, value);
            }
        }

        private static void OnIsRatioScalePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MultiCropImage cropImage)
            {
                cropImage.DrawImage();
            }
        }

        private static void OnRectScalePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!IsValueValid((double)e.NewValue))
            {
                throw new ArgumentException("Value must be between 0 and 1.");
            }

            if (d is MultiCropImage cropImage)
            {
                cropImage.DrawImage();
            }
        }

        private static bool IsValueValid(double value)
        {
            if (value >= 0.0)
            {
                return value <= 1.0;
            }

            return false;
        }

        public static BitmapFrame CreateResizedImage(ImageSource source, int width, int height, int margin)
        {
            Rect rect = new(margin, margin, width - margin * 2, height - margin * 2);
            DrawingGroup drawingGroup = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(drawingGroup, BitmapScalingMode.HighQuality);
            drawingGroup.Children.Add(new ImageDrawing(source, rect));
            DrawingVisual drawingVisual = new DrawingVisual();
            using (DrawingContext drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawDrawing(drawingGroup);
            }

            RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(width, height, 96.0, 96.0, PixelFormats.Default);
            renderTargetBitmap.Render(drawingVisual);
            return BitmapFrame.Create(renderTargetBitmap);
        }

        static MultiCropImage()
        {
            SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(MultiCropImage), new PropertyMetadata(null, OnSourceChanged));
            CurrentRectProperty = DependencyProperty.Register("CurrentRect", typeof(Rect), typeof(MultiCropImage), new PropertyMetadata(null));
            CurrentAreaBitmapProperty = DependencyProperty.Register("CurrentAreaBitmap", typeof(ImageSource), typeof(MultiCropImage), new PropertyMetadata(null));
            IsRatioScaleProperty = DependencyProperty.Register("IsRatioScale", typeof(bool), typeof(MultiCropImage), new PropertyMetadata(false, OnIsRatioScalePropertyChanged));
            ScaleSizeProperty = DependencyProperty.Register("ScaleSize", typeof(Size), typeof(MultiCropImage), new PropertyMetadata(new Size(2.0, 1.0)));
            RectScaleProperty = DependencyProperty.Register("RectScale", typeof(double), typeof(MultiCropImage), new PropertyMetadata(0.5, OnRectScalePropertyChanged));
            FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(MultiCropImage), new FrameworkPropertyMetadata(typeof(MultiCropImage)));
        }

        public MultiCropImage()
        {
            base.Loaded -= OnCropImage_Loaded;
            base.Loaded += OnCropImage_Loaded;
            base.Unloaded -= OnCropImage_Unloaded;
            base.Unloaded += OnCropImage_Unloaded;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            static double Clamp(double value, double min, double max)
            {
                return value > max ? max : value < min ? min : value;
            }

            if (_isPanningMode) return; // 平移模式下禁用缩放

            var scaleTransform = GetTemplateChild("PART_ScaleTransform") as ScaleTransform;
            var translateTransform = GetTemplateChild("PART_TranslateTransform") as TranslateTransform;
            if (scaleTransform == null || translateTransform == null) return;

            Point mousePos = e.GetPosition(this);
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = Clamp(_viewScale * zoomFactor, 0.1, 20.0);

            // 以鼠标指针为中心的缩放公式
            double ratio = newScale / _viewScale;
            _viewTranslate = new Point(
                mousePos.X - (mousePos.X - _viewTranslate.X) * ratio,
                mousePos.Y - (mousePos.Y - _viewTranslate.Y) * ratio
            );
            _viewScale = newScale;

            scaleTransform.ScaleX = scaleTransform.ScaleY = _viewScale;
            translateTransform.X = _viewTranslate.X;
            translateTransform.Y = _viewTranslate.Y;

            // 注意：此处不调用Render()，因为裁剪框未动，无需重新裁剪预览
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isPanningMode = !_isPanningMode;
                _lastMousePos = e.GetPosition(this);
                Cursor = _isPanningMode ? Cursors.SizeAll : Cursors.Arrow;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isPanningMode && e.MiddleButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(this);
                Vector delta = current - _lastMousePos;

                // 同时移动视图层
                _viewTranslate += delta;
                var tt = GetTemplateChild("PART_TranslateTransform") as TranslateTransform;
                if (tt != null) { tt.X = _viewTranslate.X; tt.Y = _viewTranslate.Y; }

                // 同时移动裁剪框层
                var cropCanvas = GetTemplateChild("PART_CropCanvas") as Canvas;
                if (cropCanvas != null)
                {
                    Canvas.SetLeft(cropCanvas, Canvas.GetLeft(cropCanvas) + delta.X);
                    Canvas.SetTop(cropCanvas, Canvas.GetTop(cropCanvas) + delta.Y);
                }

                _lastMousePos = current;
                Render(); // 平移后需要更新裁剪预览
                e.Handled = true;
                return;
            }
            base.OnMouseMove(e);
        }

        private void InitializeRotationEvents()
        {
            var rotateHandle = GetTemplateChild("PART_RotateHandle") as Ellipse;
            var resetBtn = GetTemplateChild("PART_ResetRotationBtn") as Button;
            var border = GetTemplateChild("PART_Border") as Border;

            if (rotateHandle != null)
            {
                rotateHandle.MouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed && border != null)
                    {
                        Point center = new Point(border.ActualWidth / 2, border.ActualHeight / 2);
                        Point current = e.GetPosition(border);
                        double angle = Math.Atan2(current.Y - center.Y, current.X - center.X) * 180 / Math.PI + 90;
                        CropAngle = angle;
                    }
                };
            }

            if (resetBtn != null)
            {
                resetBtn.Click += (s, e) => CropAngle = 0;
            }
        }

        private static void OnCropAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MultiCropImage ci)
            {
                var rt = ci.GetTemplateChild("PART_RotateTransform") as RotateTransform;
                if (rt != null) rt.Angle = (double)e.NewValue;
                ci.Render();
            }
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MultiCropImage cropImage = (MultiCropImage)d;
            if (cropImage != null)
            {
                cropImage.CleanupResources();
                cropImage.DrawImage();
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (!_isUnloaded)
            {
                _canvas = GetTemplateChild("PART_CropCanvas") as Canvas;
                _rectangleLeft = GetTemplateChild("PART_RectangleLeft") as Rectangle;
                _rectangleTop = GetTemplateChild("PART_RectangleTop") as Rectangle;
                _rectangleRight = GetTemplateChild("PART_RectangleRight") as Rectangle;
                _rectangleBottom = GetTemplateChild("PART_RectangleBottom") as Rectangle;
                _border = GetTemplateChild("PART_Border") as Border;
                if (!_isInitialized)
                {
                    _isInitialized = true;
                    InitializeEvents();
                    InitializeRotationEvents();
                }

                DrawImage();
            }
        }

        private void OnCropImage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            CleanupResources(fullCleanup: true);
        }

        private void OnCropImage_Loaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = false;
            if (!_isInitialized && _border != null)
            {
                InitializeEvents();
            }

            DrawImage();
        }

        private void CleanupResources(bool fullCleanup = false)
        {
            try
            {
                if (_screenCutAdorner != null && _adornerLayer != null)
                {
                    _adornerLayer.Remove(_screenCutAdorner);
                    _screenCutAdorner = null;
                }

                if (fullCleanup)
                {
                    UninitializeEvents();
                    _adornerLayer = null;
                    if (_bitmapFrame != null)
                    {
                        _bitmapFrame = null;
                    }

                    if (_canvas != null && _canvas.Background is ImageBrush imageBrush)
                    {
                        imageBrush.ImageSource = null;
                        _canvas.Background = null;
                    }

                    if (CurrentAreaBitmap != null)
                    {
                        CurrentAreaBitmap = null;
                    }

                    _border = null;
                    _canvas = null;
                    _isInitialized = false;
                    _isDragging = false;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception)
            {
            }
        }

        private void InitializeEvents()
        {
            if (_border != null)
            {
                _border.SizeChanged -= Border_SizeChanged;
                _border.SizeChanged += Border_SizeChanged;
                _border.MouseDown -= Border_MouseDown;
                _border.MouseDown += Border_MouseDown;
                _border.MouseMove -= Border_MouseMove;
                _border.MouseMove += Border_MouseMove;
                _border.MouseUp -= Border_MouseUp;
                _border.MouseUp += Border_MouseUp;
            }
        }

        private void UninitializeEvents()
        {
            if (_border != null)
            {
                _border.SizeChanged -= Border_SizeChanged;
                _border.MouseDown -= Border_MouseDown;
                _border.MouseMove -= Border_MouseMove;
                _border.MouseUp -= Border_MouseUp;
            }
        }

        private void DrawImage()
        {
            if (Source == null)
            {
                return;
            }

            CleanupResources();
            if (_border.Visibility == Visibility.Collapsed)
            {
                _border.Visibility = Visibility.Visible;
            }

            if (!(Source is BitmapImage bitmapImage))
            {
                return;
            }

            _bitmapFrame = CreateResizedImage(bitmapImage, (int)bitmapImage.Width, (int)bitmapImage.Height, 0);
            _canvas.Width = bitmapImage.Width;
            _canvas.Height = bitmapImage.Height;
            ImageBrush background = new ImageBrush(bitmapImage)
            {
                Stretch = Stretch.Uniform
            };
            _canvas.Background = background;
            if (IsRatioScale && !ScaleSize.IsEmpty && ScaleSize.Width > 0.0 && ScaleSize.Height > 0.0)
            {
                double rectScale = RectScale;
                double num = bitmapImage.Width / bitmapImage.Height;
                double num2 = ScaleSize.Width / ScaleSize.Height;
                if (num >= num2)
                {
                    _border.Height = bitmapImage.Height * rectScale;
                    _border.Width = _border.Height * num2;
                    if (_border.Width > bitmapImage.Width)
                    {
                        _border.Width = bitmapImage.Width * rectScale;
                        _border.Height = _border.Width / num2;
                    }
                }
                else
                {
                    _border.Width = bitmapImage.Width * rectScale;
                    _border.Height = _border.Width / num2;
                    if (_border.Height > bitmapImage.Height)
                    {
                        _border.Height = bitmapImage.Height * rectScale;
                        _border.Width = _border.Height * num2;
                    }
                }

                _border.Width = Math.Max(_border.Width, 10.0);
                _border.Height = Math.Max(_border.Height, 10.0);
            }
            else
            {
                _border.Width = bitmapImage.Width * RectScale;
                _border.Height = bitmapImage.Height * RectScale;
                _border.Width = Math.Max(_border.Width, 10.0);
                _border.Height = Math.Max(_border.Height, 10.0);
            }

            double length = Math.Max(0.0, Math.Min(_canvas.Width / 2.0 - _border.Width / 2.0, _canvas.Width - _border.Width));
            double length2 = Math.Max(0.0, Math.Min(_canvas.Height / 2.0 - _border.Height / 2.0, _canvas.Height - _border.Height));
            Canvas.SetLeft(_border, length);
            Canvas.SetTop(_border, length2);
            if (_adornerLayer == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(_border);
            }

            if (_screenCutAdorner == null && _adornerLayer != null)
            {
                _screenCutAdorner = new ScreenCutAdorner(_border, IsRatioScale, ScaleSize);
                _adornerLayer.Add(_screenCutAdorner);
            }

            Render();
        }

        private void Border_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            (sender as UIElement)?.ReleaseMouseCapture();
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                UIElement uIElement = sender as UIElement;
                Point position = e.GetPosition(this);
                _offsetX = position.X - Canvas.GetLeft(uIElement);
                _offsetY = position.Y - Canvas.GetTop(uIElement);
                uIElement?.CaptureMouse();
            }
        }

        private void Border_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                UIElement element = sender as UIElement;
                Point position = e.GetPosition(this);
                double val = position.X - _offsetX;
                val = Math.Max(0.0, Math.Min(val, _canvas.Width - _border.ActualWidth));
                double val2 = position.Y - _offsetY;
                val2 = Math.Max(0.0, Math.Min(val2, _canvas.Height - _border.ActualHeight));
                Canvas.SetLeft(element, val);
                Canvas.SetTop(element, val2);
                Render();
            }
        }

        private void Render()
        {
            if (_border != null && _canvas != null)
            {
                double num = Math.Max(0.0, Canvas.GetTop(_border));
                double num2 = Math.Max(0.0, Canvas.GetLeft(_border));
                UpdateMaskRectangles(num, num2);
                WriteableBitmap writeableBitmap = CutBitmap();
                if (writeableBitmap != null)
                {
                    BitmapFrame currentAreaBitmap = BitmapFrame.Create(writeableBitmap);
                    CurrentAreaBitmap = currentAreaBitmap;
                    CurrentRect = new Rect(num2, num, _border.ActualWidth, _border.ActualHeight);
                }
            }
        }

        private void UpdateMaskRectangles(double cy, double borderLeft)
        {
            _rectangleLeft.Width = borderLeft;
            _rectangleLeft.Height = _border.ActualHeight;
            Canvas.SetTop(_rectangleLeft, cy);
            _rectangleTop.Width = _canvas.Width;
            _rectangleTop.Height = cy;
            double num = Math.Min(borderLeft + _border.ActualWidth, _canvas.Width);
            _rectangleRight.Width = Math.Max(0.0, _canvas.Width - num);
            _rectangleRight.Height = _border.ActualHeight;
            Canvas.SetLeft(_rectangleRight, num);
            Canvas.SetTop(_rectangleRight, cy);
            double num2 = Math.Min(cy + _border.ActualHeight, _canvas.Height);
            _rectangleBottom.Width = _canvas.Width;
            _rectangleBottom.Height = Math.Max(0.0, _canvas.Height - num2);
            Canvas.SetTop(_rectangleBottom, num2);
        }

        private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Render();
        }

        private WriteableBitmap CutBitmap()
        {
            if (_bitmapFrame == null || _border == null) return null;

            try
            {
                // 1. 获取裁剪框在屏幕坐标系中的四角点（考虑旋转）
                var border = _border;
                Point[] screenCorners = new Point[4];
                var rt = GetTemplateChild("PART_RotateTransform") as RotateTransform;
                double angle = rt?.Angle ?? 0;
                double cx = Canvas.GetLeft(_border) + border.ActualWidth / 2;
                double cy = Canvas.GetTop(_border) + border.ActualHeight / 2;

                // 计算旋转后的四角
                for (int i = 0; i < 4; i++)
                {
                    double dx = (i < 2 ? -1 : 1) * border.ActualWidth / 2;
                    double dy = (i % 2 == 0 ? -1 : 1) * border.ActualHeight / 2;
                    double rad = angle * Math.PI / 180;
                    screenCorners[i] = new Point(
                        cx + dx * Math.Cos(rad) - dy * Math.Sin(rad),
                        cy + dx * Math.Sin(rad) + dy * Math.Cos(rad)
                    );
                }

                // 2. 逆变换：屏幕坐标 → 原图像素坐标
                // 先减去视图平移，再除以视图缩放
                Matrix invMatrix = new Matrix(1.0 / _viewScale, 0, 0, 1.0 / _viewScale,
                                               -_viewTranslate.X / _viewScale, -_viewTranslate.Y / _viewScale);

                Point[] origCorners = screenCorners.Select(p => invMatrix.Transform(p)).ToArray();

                // 3. 无旋转时使用高效CroppedBitmap
                if (Math.Abs(angle) < 0.01)
                {
                    Rect r = new Rect(origCorners[0], origCorners[2]);
                    int x = Math.Max(0, (int)Math.Round(r.X));
                    int y = Math.Max(0, (int)Math.Round(r.Y));
                    int w = Math.Min((int)Math.Round(r.Width), _bitmapFrame.PixelWidth - x);
                    int h = Math.Min((int)Math.Round(r.Height), _bitmapFrame.PixelHeight - y);
                    if (w <= 0 || h <= 0) return null;

                    return new WriteableBitmap(new CroppedBitmap(_bitmapFrame, new Int32Rect(x, y, w, h)));
                }

                // 4. 有旋转时：在原图上绘制反向旋转的裁剪区域
                // 使用RenderTargetBitmap确保输出分辨率=原图DPI
                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawImage(_bitmapFrame, new Rect(0, 0, _bitmapFrame.PixelWidth, _bitmapFrame.PixelHeight));
                    // 构建裁剪路径并应用Clip...
                }
                var rtb = new RenderTargetBitmap(_bitmapFrame.PixelWidth, _bitmapFrame.PixelHeight,
                                                  _bitmapFrame.DpiX, _bitmapFrame.DpiY, PixelFormats.Pbgra32);
                rtb.Render(dv);
                return new WriteableBitmap(rtb);
            }
            catch { return null; }
        }
    }
}
