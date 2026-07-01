using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ImageTool.Services.Tools
{
    internal class ShadeTool
    {
        private readonly CropTool _cropTool;
        private RectangleGeometry _rectangleGeo;
        RectangleGeometry _geometry1;
        Canvas _canvas;

        public Path ShadeOverlay { get; set; }

        public ShadeTool(Canvas canvas, CropTool cropTool)
        {
            _cropTool = cropTool;
            _canvas=canvas;

            ShadeOverlay = new Path
            {
                Fill = Brushes.Black,
                Opacity = 0.5
            };

            var geometryGroup = new GeometryGroup();
            _geometry1 =
                new RectangleGeometry(new Rect(new Size(canvas.Width, canvas.Height)));
            _rectangleGeo = new RectangleGeometry(
                new Rect(
                    _cropTool.TopLeftX,
                    _cropTool.TopLeftY,
                    _cropTool.Width,
                    _cropTool.Height
                )
            );
            geometryGroup.Children.Add(_geometry1);
            geometryGroup.Children.Add(_rectangleGeo);
            ShadeOverlay.Data = geometryGroup;
        }

        public void Resize() 
        {
            var geometryGroup = new GeometryGroup();
            _geometry1 =
                new RectangleGeometry(new Rect(new Size(_canvas.Width, _canvas.Height)));
            _rectangleGeo = new RectangleGeometry(
                new Rect(
                    _cropTool.TopLeftX,
                    _cropTool.TopLeftY,
                    _cropTool.Width,
                    _cropTool.Height
                )
            );
            geometryGroup.Children.Add(_geometry1);
            geometryGroup.Children.Add(_rectangleGeo);
            ShadeOverlay.Data = geometryGroup;
        }

        public void Redraw()
        {
            _rectangleGeo.Rect = new Rect(
                _cropTool.TopLeftX,
                _cropTool.TopLeftY,
                _cropTool.Width,
                _cropTool.Height
            );
        }
    }
}
