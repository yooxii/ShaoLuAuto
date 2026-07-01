using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using System.Windows.Ink;

namespace ImageTool.Controls
{
    internal class InkCanvasEx:InkCanvas
    {
        Stack<StrokeCollection> strokeList = new Stack<StrokeCollection>();

        public InkCanvasEx() 
        {
            this.Strokes.StrokesChanged += Strokes_StrokesChanged;
        }

        private void Strokes_StrokesChanged(object sender, System.Windows.Ink.StrokeCollectionChangedEventArgs e)
        {
            if (e.Added.Count > 0)
            {
                strokeList.Push(e.Added);
            }
        }

        public void Undo() 
        {
            if (strokeList.Count > 0)
            {
                this.Strokes.Remove(strokeList.Pop());
            }
        }

    }
}
