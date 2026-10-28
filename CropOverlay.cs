#pragma warning disable 618
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CleanImageViewer
{
    public class CropOverlay : FrameworkElement
    {
        public event Action<Int32Rect> CropConfirmed;
        public event Action CropCancelled;

        private enum DragMode
        {
            None,
            Creating,
            Moving,
            ResizeTopLeft,
            ResizeTop,
            ResizeTopRight,
            ResizeRight,
            ResizeBottomRight,
            ResizeBottom,
            ResizeBottomLeft,
            ResizeLeft
        }

        private DragMode _currentDrag = DragMode.None;
        private Point _dragStartScreen;
        private Rect _startImageRect;

        // The crop rectangle in IMAGE PIXELS
        private Rect _cropImageRect = Rect.Empty;

        private double _imageWidth = 0;
        private double _imageHeight = 0;
        private double _scale = 1.0;
        private Point _offset = new Point(0, 0);

        private const double HandleSize = 10.0;
        private readonly Pen _borderPen;
        private readonly Pen _dashedPen;
        private readonly Brush _handleBrush;
        private readonly Pen _handlePen;
        private readonly Brush _dimBrush;
        private readonly Brush _badgeBgBrush;
        private readonly Typeface _typeface;

        public CropOverlay()
        {
            _borderPen = new Pen(Brushes.White, 1.5);
            _borderPen.Freeze();

            _dashedPen = new Pen(Brushes.Black, 1.5);
            _dashedPen.DashStyle = DashStyles.Dash;
            _dashedPen.Freeze();

            _handleBrush = Brushes.White;
            _handleBrush.Freeze();

            _handlePen = new Pen(Brushes.Black, 1.0);
            _handlePen.Freeze();

            _dimBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
            _dimBrush.Freeze();

            _badgeBgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
            _badgeBgBrush.Freeze();

            _typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

            Focusable = true;
            ClipToBounds = true;
        }

        public void Initialize(double imgWidth, double imgHeight, double scale, Point offset)
        {
            _imageWidth = imgWidth;
            _imageHeight = imgHeight;
            _scale = scale;
            _offset = offset;

            // Default crop box: center 80% of image
            if (imgWidth > 0 && imgHeight > 0)
            {
                double w = Math.Round(imgWidth * 0.8);
                double h = Math.Round(imgHeight * 0.8);
                double x = Math.Round((imgWidth - w) / 2.0);
                double y = Math.Round((imgHeight - h) / 2.0);
                _cropImageRect = new Rect(x, y, w, h);
            }
            else
            {
                _cropImageRect = Rect.Empty;
            }

            InvalidateVisual();
            Focus();
        }

        public void UpdateViewportTransform(double scale, Point offset)
        {
            _scale = scale;
            _offset = offset;
            InvalidateVisual();
        }

        public Rect GetImageCropRect()
        {
            return _cropImageRect;
        }

        private Rect ImageToScreenRect(Rect imgRect)
        {
            if (imgRect.IsEmpty) return Rect.Empty;
            return new Rect(
                imgRect.X * _scale + _offset.X,
                imgRect.Y * _scale + _offset.Y,
                imgRect.Width * _scale,
                imgRect.Height * _scale);
        }

        private Point ScreenToImagePoint(Point screenPt)
        {
            if (_scale <= 0) return new Point(0, 0);
            double x = (screenPt.X - _offset.X) / _scale;
            double y = (screenPt.Y - _offset.Y) / _scale;
            return new Point(x, y);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double viewW = ActualWidth;
            double viewH = ActualHeight;
            if (viewW <= 0 || viewH <= 0) return;

            Rect screenCrop = ImageToScreenRect(_cropImageRect);

            // If empty, dim everything lightly
            if (screenCrop.IsEmpty || screenCrop.Width <= 1 || screenCrop.Height <= 1)
            {
                dc.DrawRectangle(_dimBrush, null, new Rect(0, 0, viewW, viewH));
                return;
            }

            // Draw 4 dimmed rectangles around the selection
            // Top
            if (screenCrop.Top > 0)
                dc.DrawRectangle(_dimBrush, null, new Rect(0, 0, viewW, Math.Max(0, screenCrop.Top)));
            // Bottom
            if (screenCrop.Bottom < viewH)
                dc.DrawRectangle(_dimBrush, null, new Rect(0, screenCrop.Bottom, viewW, Math.Max(0, viewH - screenCrop.Bottom)));
            // Left
            if (screenCrop.Left > 0)
                dc.DrawRectangle(_dimBrush, null, new Rect(0, Math.Max(0, screenCrop.Top), Math.Max(0, screenCrop.Left), screenCrop.Height));
            // Right
            if (screenCrop.Right < viewW)
                dc.DrawRectangle(_dimBrush, null, new Rect(screenCrop.Right, Math.Max(0, screenCrop.Top), Math.Max(0, viewW - screenCrop.Right), screenCrop.Height));

            // Crop borders (solid white + dashed black)
            dc.DrawRectangle(null, _borderPen, screenCrop);
            dc.DrawRectangle(null, _dashedPen, screenCrop);

            // Rule of thirds subtle grid lines inside crop box
            if (screenCrop.Width > 60 && screenCrop.Height > 60)
            {
                Pen guidePen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1.0);
                guidePen.DashStyle = DashStyles.Dot;
                guidePen.Freeze();

                double x1 = screenCrop.Left + screenCrop.Width / 3.0;
                double x2 = screenCrop.Left + (screenCrop.Width * 2.0) / 3.0;
                double y1 = screenCrop.Top + screenCrop.Height / 3.0;
                double y2 = screenCrop.Top + (screenCrop.Height * 2.0) / 3.0;

                dc.DrawLine(guidePen, new Point(x1, screenCrop.Top), new Point(x1, screenCrop.Bottom));
                dc.DrawLine(guidePen, new Point(x2, screenCrop.Top), new Point(x2, screenCrop.Bottom));
                dc.DrawLine(guidePen, new Point(screenCrop.Left, y1), new Point(screenCrop.Right, y1));
                dc.DrawLine(guidePen, new Point(screenCrop.Left, y2), new Point(screenCrop.Right, y2));
            }

            // Draw 8 handles
            DrawHandle(dc, screenCrop.Left, screenCrop.Top);
            DrawHandle(dc, screenCrop.Left + screenCrop.Width / 2.0, screenCrop.Top);
            DrawHandle(dc, screenCrop.Right, screenCrop.Top);
            DrawHandle(dc, screenCrop.Right, screenCrop.Top + screenCrop.Height / 2.0);
            DrawHandle(dc, screenCrop.Right, screenCrop.Bottom);
            DrawHandle(dc, screenCrop.Left + screenCrop.Width / 2.0, screenCrop.Bottom);
            DrawHandle(dc, screenCrop.Left, screenCrop.Bottom);
            DrawHandle(dc, screenCrop.Left, screenCrop.Top + screenCrop.Height / 2.0);

            // Dimension Badge
            int pxW = (int)Math.Max(1, Math.Round(_cropImageRect.Width));
            int pxH = (int)Math.Max(1, Math.Round(_cropImageRect.Height));
            string badgeText = string.Format("{0} × {1} px  [Enter: Apply | Esc: Cancel]", pxW, pxH);

            FormattedText ft = new FormattedText(
                badgeText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                11.0,
                Brushes.White);

            double badgeX = screenCrop.Left + (screenCrop.Width - ft.Width) / 2.0;
            double badgeY = screenCrop.Bottom + 8;
            if (badgeY + ft.Height + 8 > viewH)
            {
                badgeY = screenCrop.Top - ft.Height - 14;
                if (badgeY < 8) badgeY = screenCrop.Top + 8;
            }

            Rect badgeRect = new Rect(badgeX - 8, badgeY - 3, ft.Width + 16, ft.Height + 6);
            dc.DrawRoundedRectangle(_badgeBgBrush, new Pen(Brushes.Gray, 1), badgeRect, 4, 4);
            dc.DrawText(ft, new Point(badgeX, badgeY));
        }

        private void DrawHandle(DrawingContext dc, double cx, double cy)
        {
            Rect handleRect = new Rect(cx - HandleSize / 2.0, cy - HandleSize / 2.0, HandleSize, HandleSize);
            dc.DrawRectangle(_handleBrush, _handlePen, handleRect);
        }

        private DragMode HitTest(Point screenPt)
        {
            if (_cropImageRect.IsEmpty) return DragMode.None;

            Rect screenCrop = ImageToScreenRect(_cropImageRect);
            double hHalf = HandleSize / 2.0 + 3.0;

            if (new Rect(screenCrop.Left - hHalf, screenCrop.Top - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeTopLeft;
            if (new Rect(screenCrop.Right - hHalf, screenCrop.Top - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeTopRight;
            if (new Rect(screenCrop.Right - hHalf, screenCrop.Bottom - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeBottomRight;
            if (new Rect(screenCrop.Left - hHalf, screenCrop.Bottom - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeBottomLeft;

            if (new Rect(screenCrop.Left + screenCrop.Width / 2.0 - hHalf, screenCrop.Top - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeTop;
            if (new Rect(screenCrop.Right - hHalf, screenCrop.Top + screenCrop.Height / 2.0 - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeRight;
            if (new Rect(screenCrop.Left + screenCrop.Width / 2.0 - hHalf, screenCrop.Bottom - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeBottom;
            if (new Rect(screenCrop.Left - hHalf, screenCrop.Top + screenCrop.Height / 2.0 - hHalf, HandleSize + 6, HandleSize + 6).Contains(screenPt))
                return DragMode.ResizeLeft;

            if (screenCrop.Contains(screenPt))
                return DragMode.Moving;

            return DragMode.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point pt = e.GetPosition(this);

            if (_currentDrag == DragMode.None)
            {
                DragMode hit = HitTest(pt);
                switch (hit)
                {
                    case DragMode.ResizeTopLeft:
                    case DragMode.ResizeBottomRight:
                        Cursor = Cursors.SizeNWSE;
                        break;
                    case DragMode.ResizeTopRight:
                    case DragMode.ResizeBottomLeft:
                        Cursor = Cursors.SizeNESW;
                        break;
                    case DragMode.ResizeTop:
                    case DragMode.ResizeBottom:
                        Cursor = Cursors.SizeNS;
                        break;
                    case DragMode.ResizeLeft:
                    case DragMode.ResizeRight:
                        Cursor = Cursors.SizeWE;
                        break;
                    case DragMode.Moving:
                        Cursor = Cursors.SizeAll;
                        break;
                    default:
                        Cursor = Cursors.Cross;
                        break;
                }
            }
            else
            {
                Point currentImgPt = ScreenToImagePoint(pt);
                Point startImgPt = ScreenToImagePoint(_dragStartScreen);
                double dx = currentImgPt.X - startImgPt.X;
                double dy = currentImgPt.Y - startImgPt.Y;

                if (_currentDrag == DragMode.Creating)
                {
                    double x1 = Math.Max(0, Math.Min(_imageWidth, Math.Min(startImgPt.X, currentImgPt.X)));
                    double y1 = Math.Max(0, Math.Min(_imageHeight, Math.Min(startImgPt.Y, currentImgPt.Y)));
                    double x2 = Math.Max(0, Math.Min(_imageWidth, Math.Max(startImgPt.X, currentImgPt.X)));
                    double y2 = Math.Max(0, Math.Min(_imageHeight, Math.Max(startImgPt.Y, currentImgPt.Y)));
                    _cropImageRect = new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                }
                else if (_currentDrag == DragMode.Moving)
                {
                    double newX = Math.Max(0, Math.Min(_imageWidth - _startImageRect.Width, _startImageRect.X + dx));
                    double newY = Math.Max(0, Math.Min(_imageHeight - _startImageRect.Height, _startImageRect.Y + dy));
                    _cropImageRect = new Rect(newX, newY, _startImageRect.Width, _startImageRect.Height);
                }
                else
                {
                    double left = _startImageRect.Left;
                    double top = _startImageRect.Top;
                    double right = _startImageRect.Right;
                    double bottom = _startImageRect.Bottom;

                    switch (_currentDrag)
                    {
                        case DragMode.ResizeTopLeft:
                            left = Math.Max(0, Math.Min(right - 5, _startImageRect.Left + dx));
                            top = Math.Max(0, Math.Min(bottom - 5, _startImageRect.Top + dy));
                            break;
                        case DragMode.ResizeTop:
                            top = Math.Max(0, Math.Min(bottom - 5, _startImageRect.Top + dy));
                            break;
                        case DragMode.ResizeTopRight:
                            right = Math.Min(_imageWidth, Math.Max(left + 5, _startImageRect.Right + dx));
                            top = Math.Max(0, Math.Min(bottom - 5, _startImageRect.Top + dy));
                            break;
                        case DragMode.ResizeRight:
                            right = Math.Min(_imageWidth, Math.Max(left + 5, _startImageRect.Right + dx));
                            break;
                        case DragMode.ResizeBottomRight:
                            right = Math.Min(_imageWidth, Math.Max(left + 5, _startImageRect.Right + dx));
                            bottom = Math.Min(_imageHeight, Math.Max(top + 5, _startImageRect.Bottom + dy));
                            break;
                        case DragMode.ResizeBottom:
                            bottom = Math.Min(_imageHeight, Math.Max(top + 5, _startImageRect.Bottom + dy));
                            break;
                        case DragMode.ResizeBottomLeft:
                            left = Math.Max(0, Math.Min(right - 5, _startImageRect.Left + dx));
                            bottom = Math.Min(_imageHeight, Math.Max(top + 5, _startImageRect.Bottom + dy));
                            break;
                        case DragMode.ResizeLeft:
                            left = Math.Max(0, Math.Min(right - 5, _startImageRect.Left + dx));
                            break;
                    }

                    _cropImageRect = new Rect(left, top, right - left, bottom - top);
                }

                InvalidateVisual();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    // Double click confirms crop
                    ApplyCrop();
                    e.Handled = true;
                    return;
                }

                Point pt = e.GetPosition(this);
                _dragStartScreen = pt;
                _startImageRect = _cropImageRect;
                DragMode hit = HitTest(pt);

                if (hit != DragMode.None)
                {
                    _currentDrag = hit;
                }
                else
                {
                    _currentDrag = DragMode.Creating;
                    Point imgPt = ScreenToImagePoint(pt);
                    _cropImageRect = new Rect(imgPt.X, imgPt.Y, 1, 1);
                }

                CaptureMouse();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                // Right click inside crop overlay cancels crop mode
                CancelCrop();
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == MouseButton.Left && _currentDrag != DragMode.None)
            {
                _currentDrag = DragMode.None;
                ReleaseMouseCapture();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Enter)
            {
                ApplyCrop();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelCrop();
                e.Handled = true;
            }
        }

        public void ApplyCrop()
        {
            if (_cropImageRect.Width >= 1 && _cropImageRect.Height >= 1)
            {
                Int32Rect rect = new Int32Rect(
                    (int)Math.Max(0, Math.Round(_cropImageRect.X)),
                    (int)Math.Max(0, Math.Round(_cropImageRect.Y)),
                    (int)Math.Max(1, Math.Round(_cropImageRect.Width)),
                    (int)Math.Max(1, Math.Round(_cropImageRect.Height)));

                if (CropConfirmed != null)
                {
                    CropConfirmed(rect);
                }
            }
        }

        public void CancelCrop()
        {
            if (CropCancelled != null)
            {
                CropCancelled();
            }
        }
    }
}
