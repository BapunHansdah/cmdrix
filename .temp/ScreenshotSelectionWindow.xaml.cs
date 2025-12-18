using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace cmdrix
{
    public partial class ScreenshotSelectionWindow : Window
    {
        private Point _startPoint;
        private bool _isSelecting;
        public System.Drawing.Rectangle? SelectedArea { get; private set; }
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        public ScreenshotSelectionWindow()
        {
            InitializeComponent();

            // Get DPI scaling
            Loaded += (s, e) => {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                Canvas.SetLeft(ControlPanel, (ActualWidth - ControlPanel.ActualWidth) / 2);
                Canvas.SetTop(ControlPanel, ActualHeight - 80);

                Canvas.SetLeft(InstructionText, (ActualWidth - InstructionText.ActualWidth) / 2);
            };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _isSelecting = true;
            SelectionRectangle.Visibility = Visibility.Visible;
            InstructionText.Text = "Release to complete selection";

            // Position the rectangle at start point
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            var currentPoint = e.GetPosition(this);

            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRectangle, x);
            Canvas.SetTop(SelectionRectangle, y);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;

            _isSelecting = false;

            var currentPoint = e.GetPosition(this);

            // Calculate selection in WPF coordinates
            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            // Only enable confirm if area is large enough
            if (width > 10 && height > 10)
            {
                // Convert to physical screen coordinates using DPI scaling
                var physicalX = (int)(x * _dpiScaleX);
                var physicalY = (int)(y * _dpiScaleY);
                var physicalWidth = (int)(width * _dpiScaleX);
                var physicalHeight = (int)(height * _dpiScaleY);

                SelectedArea = new System.Drawing.Rectangle(physicalX, physicalY, physicalWidth, physicalHeight);
                ConfirmButton.IsEnabled = true;
                InstructionText.Text = $"Selected: {physicalWidth}x{physicalHeight} px";
            }
            else
            {
                SelectionRectangle.Visibility = Visibility.Collapsed;
                ConfirmButton.IsEnabled = false;
                InstructionText.Text = "Selection too small. Drag to select an area";
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}