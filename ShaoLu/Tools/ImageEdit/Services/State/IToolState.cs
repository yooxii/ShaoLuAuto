using System.Windows;

namespace ImageTool.Services.State
{
    internal interface IToolState
    {
        void OnMouseDown(Point point);
        Position? OnMouseMove(Point point);
        void OnMouseUp(Point point);
    }
}