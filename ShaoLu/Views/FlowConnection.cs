using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nodify;

namespace ShaoLu.Views
{
    /// <summary>
    /// Nodify 连线控件：按走线点画正交折线，拐角切成圆角，末端箭头沿用 Nodify 的画法。
    /// 走线点由 <see cref="FlowRouteGrid"/> 算好后塞进 <see cref="Points"/>。
    /// </summary>
    public sealed class FlowConnection : BaseConnection
    {
        public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
            nameof(Points),
            typeof(PointCollection),
            typeof(FlowConnection),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>拐角圆角半径</summary>
        public double CornerRadius { get; set; } = 8d;

        public PointCollection Points
        {
            get => (PointCollection)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        protected override ((Point ArrowStartSource, Point ArrowStartTarget), (Point ArrowEndSource, Point ArrowEndTarget)) DrawLineGeometry(
            StreamGeometryContext context, Point source, Point target)
        {
            var points = Points;            if (points == null || points.Count < 2)
            {
                context.BeginFigure(source, false, false);
                context.LineTo(target, true, true);
                return ((source, target), (source, target));
            }

            DrawRounded(context, points, Math.Max(0d, CornerRadius));
            return ((source, target), (source, target));
        }

        private static void DrawRounded(StreamGeometryContext context, IList<Point> points, double radius)
        {
            context.BeginFigure(points[0], false, false);

            for (int i = 1; i < points.Count - 1; i++)
            {
                var previous = points[i - 1];
                var corner = points[i];
                var next = points[i + 1];

                double inLength = Math.Abs(previous.X - corner.X) + Math.Abs(previous.Y - corner.Y);
                double outLength = Math.Abs(next.X - corner.X) + Math.Abs(next.Y - corner.Y);
                double r = Math.Min(radius, Math.Min(inLength, outLength) / 2d);
                if (r <= 0.5d)
                {
                    context.LineTo(corner, true, true);
                    continue;
                }

                var entry = MoveTowards(corner, previous, r);
                var exit = MoveTowards(corner, next, r);
                context.LineTo(entry, true, true);
                context.QuadraticBezierTo(corner, exit, true, true);
            }

            context.LineTo(points[points.Count - 1], true, true);
        }

        private static Point MoveTowards(Point from, Point to, double distance)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double length = Math.Abs(dx) + Math.Abs(dy);
            if (length <= 0.0001d) return from;
            return new Point(from.X + dx / length * distance, from.Y + dy / length * distance);
        }
    }
}
