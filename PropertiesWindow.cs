using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanImageViewer
{
    public class PropertiesWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly string _filePath;
        private readonly BitmapSource _bitmap;
        private readonly StringBuilder _allInfoText = new StringBuilder();

        public PropertiesWindow(Window owner, string filePath, BitmapSource bitmap)
        {
            Owner = owner;
            _filePath = filePath;
            _bitmap = bitmap;

            Title = "Image & File Properties";
            Width = 460;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            SourceInitialized += delegate
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    int useDark = 1;
                    if (DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
                    }
                }
                catch { }
            };

            BuildUI();
        }

        private void BuildUI()
        {
            Grid mainGrid = new Grid();
            mainGrid.Margin = new Thickness(16);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            StackPanel headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            TextBlock titleText = new TextBlock
            {
                Text = "Image Properties",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            TextBlock subText = new TextBlock
            {
                Text = !string.IsNullOrEmpty(_filePath) ? Path.GetFileName(_filePath) : "Unsaved Image",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerPanel.Children.Add(titleText);
            headerPanel.Children.Add(subText);
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            // Table in ScrollViewer
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 24))
            };
            Border scrollBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = scroll
            };

            StackPanel listPanel = new StackPanel { Margin = new Thickness(8) };
            scroll.Content = listPanel;

            // Collect Data
            int width = _bitmap != null ? _bitmap.PixelWidth : 0;
            int height = _bitmap != null ? _bitmap.PixelHeight : 0;
            double megapixels = (width * height) / 1000000.0;
            string aspectRatio = CalculateAspectRatio(width, height);

            AddSectionHeader(listPanel, "IMAGE INFORMATION");
            AddPropertyRow(listPanel, "Dimensions", string.Format("{0} × {1} pixels", width, height));
            AddPropertyRow(listPanel, "Megapixels", string.Format("{0:0.00} MP", megapixels));
            AddPropertyRow(listPanel, "Aspect Ratio", aspectRatio);
            if (_bitmap != null)
            {
                AddPropertyRow(listPanel, "Pixel Format", _bitmap.Format.ToString());
                AddPropertyRow(listPanel, "Bits Per Pixel", string.Format("{0} bits", _bitmap.Format.BitsPerPixel));
                AddPropertyRow(listPanel, "DPI Resolution", string.Format("{0:0} × {1:0} DPI", _bitmap.DpiX, _bitmap.DpiY));
                bool hasAlpha = _bitmap.Format == PixelFormats.Bgra32 || _bitmap.Format == PixelFormats.Pbgra32 || _bitmap.Format == PixelFormats.Rgba64;
                AddPropertyRow(listPanel, "Alpha / Transparency", hasAlpha ? "Yes" : "No");
            }

            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                FileInfo fi = new FileInfo(_filePath);
                AddSectionHeader(listPanel, "FILE INFORMATION");
                AddPropertyRow(listPanel, "File Name", fi.Name);
                AddPropertyRow(listPanel, "File Format", fi.Extension.ToUpperInvariant());
                AddPropertyRow(listPanel, "File Size", FormatFileSize(fi.Length));
                AddPropertyRow(listPanel, "Folder", fi.DirectoryName);
                AddPropertyRow(listPanel, "Full Path", fi.FullName);
                AddPropertyRow(listPanel, "Created", fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddPropertyRow(listPanel, "Modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddPropertyRow(listPanel, "Accessed", fi.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddPropertyRow(listPanel, "Attributes", fi.Attributes.ToString());
            }

            Grid.SetRow(scrollBorder, 1);
            mainGrid.Children.Add(scrollBorder);

            // Bottom Buttons
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Button copyBtn = CreateStyledButton("Copy Info");
            copyBtn.Click += delegate
            {
                try
                {
                    Clipboard.SetText(_allInfoText.ToString());
                    copyBtn.Content = "Copied!";
                }
                catch { }
            };

            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                Button explorerBtn = CreateStyledButton("Show in Explorer");
                explorerBtn.Click += delegate
                {
                    try
                    {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", _filePath));
                    }
                    catch { }
                };
                buttonPanel.Children.Add(explorerBtn);
            }

            Button closeBtn = CreateStyledButton("Close");
            closeBtn.Click += delegate { Close(); };

            buttonPanel.Children.Add(copyBtn);
            buttonPanel.Children.Add(closeBtn);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private void AddSectionHeader(StackPanel panel, string title)
        {
            TextBlock tb = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 255)),
                Margin = new Thickness(4, 10, 4, 4)
            };
            panel.Children.Add(tb);
            _allInfoText.AppendLine("[" + title + "]");
        }

        private void AddPropertyRow(StackPanel panel, string name, string value)
        {
            Grid row = new Grid { Margin = new Thickness(4, 3, 4, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock nameBlock = new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170))
            };

            TextBox valueBlock = new TextBox
            {
                Text = value,
                FontSize = 12,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap
            };

            Grid.SetColumn(nameBlock, 0);
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(nameBlock);
            row.Children.Add(valueBlock);
            panel.Children.Add(row);

            _allInfoText.AppendLine(string.Format("{0}: {1}", name, value));
        }

        private Button CreateStyledButton(string text)
        {
            Button btn = new Button
            {
                Content = text,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            return btn;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return string.Format("{0} bytes", bytes);
            if (bytes < 1024 * 1024) return string.Format("{0:0.##} KB ({1:N0} bytes)", bytes / 1024.0, bytes);
            return string.Format("{0:0.##} MB ({1:N0} bytes)", bytes / (1024.0 * 1024.0), bytes);
        }

        private static string CalculateAspectRatio(int width, int height)
        {
            if (width <= 0 || height <= 0) return "N/A";
            int gcd = GCD(width, height);
            int aspectW = width / gcd;
            int aspectH = height / gcd;
            double ratio = (double)width / height;
            return string.Format("{0}:{1} ({2:0.00}:1)", aspectW, aspectH, ratio);
        }

        private static int GCD(int a, int b)
        {
            while (b != 0)
            {
                int rem = a % b;
                a = b;
                b = rem;
            }
            return a;
        }
    }
}
