#nullable enable
using System;
using System.Windows;
using System.Windows.Media;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class PdfViewportController
    {
        public double ZoomScale { get; private set; } = 1.0;
        public double TranslateX { get; private set; } = 0.0;
        public double TranslateY { get; private set; } = 0.0;
        public bool IsPanning { get; private set; }
        private Point _panStart;

        public Transform BuildTransform()
        {
            var g = new TransformGroup();
            g.Children.Add(new ScaleTransform(ZoomScale, ZoomScale));
            g.Children.Add(new TranslateTransform(TranslateX, TranslateY));
            return g;
        }

        public void FitToView(double containerW, double containerH, double contentW, double contentH)
        {
            if (containerW <= 0 || containerH <= 0 || contentW <= 0 || contentH <= 0) return;
            ZoomScale = Math.Min(containerW / contentW, containerH / contentH);
            TranslateX = (containerW - contentW * ZoomScale) / 2.0;
            TranslateY = (containerH - contentH * ZoomScale) / 2.0;
        }

        public void ZoomAround(double factor, double pivotX, double pivotY)
        {
            double newScale = Math.Max(0.05, Math.Min(30.0, ZoomScale * factor));
            double ratio = newScale / ZoomScale;
            TranslateX = pivotX - ratio * (pivotX - TranslateX);
            TranslateY = pivotY - ratio * (pivotY - TranslateY);
            ZoomScale = newScale;
        }

        public void BeginPan(Point start) { IsPanning = true; _panStart = start; }

        public bool UpdatePan(Point current)
        {
            if (!IsPanning) return false;
            TranslateX += current.X - _panStart.X;
            TranslateY += current.Y - _panStart.Y;
            _panStart = current;
            return true;
        }

        public void EndPan() => IsPanning = false;
    }
}
