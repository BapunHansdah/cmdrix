using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace cmdrix.UI
{
    public partial class ScreenshotSelectionWindow : Window
    {
        // P/Invoke declarations
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, 
            IntPtr hdcSource, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        private const int SRCCOPY = 0x00CC0020;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        // Fields
        private Bitmap _fullScreenshot;
        private bool _isSelecting = false;
        private System.Windows.Point _selectionStart;
        private System.Windows.Point _selectionEnd;

        public Bitmap SelectedScreenshot { get; private set; }
        public bool WasCancelled { get; private set; } = true;

        public ScreenshotSelectionWindow()
        {
            InitializeComponent();
            
            // Set window to fullscreen
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.Topmost = true;
            this.ShowInTaskbar = false;
            
            Loaded += ScreenshotSelectionWindow_Loaded;
        }

        private async void ScreenshotSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Make window uncapturable
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            // Small delay to ensure window is rendered
            await System.Threading.Tasks.Task.Delay(100);

            // Capture screen
            CaptureFullScreen();
        }

        private void CaptureFullScreen()
        {
            try
            {
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);

                IntPtr hdcScreen = GetDC(IntPtr.Zero);
                IntPtr hdcMemDC = CreateCompatibleDC(hdcScreen);
                IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, screenWidth, screenHeight);
                IntPtr hOld = SelectObject(hdcMemDC, hBitmap);

                BitBlt(hdcMemDC, 0, 0, screenWidth, screenHeight, hdcScreen, 0, 0, SRCCOPY);
                SelectObject(hdcMemDC, hOld);

                _fullScreenshot = System.Drawing.Image.FromHbitmap(hBitmap);

                DeleteObject(hBitmap);
                DeleteDC(hdcMemDC);
                ReleaseDC(IntPtr.Zero, hdcScreen);

                DisplayScreenshot();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to capture screen: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void DisplayScreenshot()
        {
            using (var memory = new MemoryStream())
            {
                _fullScreenshot.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                ScreenshotImage.Source = bitmapImage;
            }
        }

        private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSelecting = true;
            _selectionStart = e.GetPosition(SelectionCanvas);
            _selectionEnd = _selectionStart;

            SelectionRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRectangle, _selectionStart.X);
            Canvas.SetTop(SelectionRectangle, _selectionStart.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;

            SelectionCanvas.CaptureMouse();
        }

        private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                _selectionEnd = e.GetPosition(SelectionCanvas);
                UpdateSelectionRectangle();
            }
        }

        private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                _selectionEnd = e.GetPosition(SelectionCanvas);
                UpdateSelectionRectangle();

                double width = Math.Abs(_selectionEnd.X - _selectionStart.X);
                double height = Math.Abs(_selectionEnd.Y - _selectionStart.Y);

                if (width > 10 && height > 10)
                {
                    // Valid selection
                    ConfirmButton.IsEnabled = true;
                    UpdateDimOverlays();
                }
                else
                {
                    // Selection too small
                    ResetSelection();
                }

                SelectionCanvas.ReleaseMouseCapture();
            }
        }

        private void UpdateSelectionRectangle()
        {
            double left = Math.Min(_selectionStart.X, _selectionEnd.X);
            double top = Math.Min(_selectionStart.Y, _selectionEnd.Y);
            double width = Math.Abs(_selectionEnd.X - _selectionStart.X);
            double height = Math.Abs(_selectionEnd.Y - _selectionStart.Y);

            Canvas.SetLeft(SelectionRectangle, left);
            Canvas.SetTop(SelectionRectangle, top);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
        }

        private void UpdateDimOverlays()
        {
            double left = Canvas.GetLeft(SelectionRectangle);
            double top = Canvas.GetTop(SelectionRectangle);
            double width = SelectionRectangle.Width;
            double height = SelectionRectangle.Height;
            double canvasWidth = SelectionCanvas.ActualWidth;
            double canvasHeight = SelectionCanvas.ActualHeight;

            // Top overlay
            Canvas.SetLeft(DimOverlay1, 0);
            Canvas.SetTop(DimOverlay1, 0);
            DimOverlay1.Width = canvasWidth;
            DimOverlay1.Height = top;
            DimOverlay1.Visibility = Visibility.Visible;

            // Bottom overlay
            Canvas.SetLeft(DimOverlay2, 0);
            Canvas.SetTop(DimOverlay2, top + height);
            DimOverlay2.Width = canvasWidth;
            DimOverlay2.Height = canvasHeight - (top + height);
            DimOverlay2.Visibility = Visibility.Visible;

            // Left overlay
            Canvas.SetLeft(DimOverlay3, 0);
            Canvas.SetTop(DimOverlay3, top);
            DimOverlay3.Width = left;
            DimOverlay3.Height = height;
            DimOverlay3.Visibility = Visibility.Visible;

            // Right overlay
            Canvas.SetLeft(DimOverlay4, left + width);
            Canvas.SetTop(DimOverlay4, top);
            DimOverlay4.Width = canvasWidth - (left + width);
            DimOverlay4.Height = height;
            DimOverlay4.Visibility = Visibility.Visible;
        }

        private void ResetSelection()
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
            DimOverlay1.Visibility = Visibility.Collapsed;
            DimOverlay2.Visibility = Visibility.Collapsed;
            DimOverlay3.Visibility = Visibility.Collapsed;
            DimOverlay4.Visibility = Visibility.Collapsed;
            ConfirmButton.IsEnabled = false;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            CropAndSave();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            this.Close();
        }

        private void CropAndSave()
        {
            try
            {
                double left = Canvas.GetLeft(SelectionRectangle);
                double top = Canvas.GetTop(SelectionRectangle);
                double width = SelectionRectangle.Width;
                double height = SelectionRectangle.Height;

                // Calculate scaling factors
                double scaleX = _fullScreenshot.Width / SelectionCanvas.ActualWidth;
                double scaleY = _fullScreenshot.Height / SelectionCanvas.ActualHeight;

                // Convert to image coordinates
                int cropX = (int)(left * scaleX);
                int cropY = (int)(top * scaleY);
                int cropWidth = (int)(width * scaleX);
                int cropHeight = (int)(height * scaleY);

                // Ensure values are within bounds
                cropX = Math.Max(0, Math.Min(cropX, _fullScreenshot.Width - 1));
                cropY = Math.Max(0, Math.Min(cropY, _fullScreenshot.Height - 1));
                cropWidth = Math.Max(1, Math.Min(cropWidth, _fullScreenshot.Width - cropX));
                cropHeight = Math.Max(1, Math.Min(cropHeight, _fullScreenshot.Height - cropY));

                // Crop the bitmap
                var cropRect = new System.Drawing.Rectangle(cropX, cropY, cropWidth, cropHeight);
                SelectedScreenshot = _fullScreenshot.Clone(cropRect, _fullScreenshot.PixelFormat);

                WasCancelled = false;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to crop screenshot: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                WasCancelled = true;
                this.Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelButton_Click(null, null);
            }
            else if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
            {
                ConfirmButton_Click(null, null);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _fullScreenshot?.Dispose();
            base.OnClosed(e);
        }
    }
}