using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CleanImageViewer
{
    public class MainWindow : Window
    {
        // UI Elements
        private Grid _rootGrid;
        private Grid _viewportGrid;
        private Canvas _imageCanvas;
        private Image _imageControl;
        private TransformGroup _transformGroup;
        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        private Border _hudBorder;
        private TextBlock _hudText;
        private Storyboard _hudAnimation;
        private StackPanel _emptyStatePanel;
        private CropOverlay _cropOverlay;

        // Image State
        private BitmapSource _currentBitmap;
        private BitmapSource _originalBitmap;
        private string _currentFilePath;
        private bool _isModified = false;
        private readonly Stack<BitmapSource> _undoStack = new Stack<BitmapSource>();

        // Navigation State
        private List<string> _folderFiles = new List<string>();
        private int _currentFileIndex = -1;

        // Zip Archive State
        private bool _isZipMode = false;
        private string _currentZipFilePath = null;
        private string _currentZipEntryName = null;
        private byte[] _currentZipEntryBytes = null;

        // Zoom & Pan State
        private double _scale = 1.0;
        private Point _offset = new Point(0, 0);
        private bool _isPanning = false;
        private Point _panStartMouse;
        private Point _panStartOffset;
        private bool _isCropMode = false;

        // Visual options
        private bool _useCheckerboard = false;
        private Brush _darkBrush;
        private Brush _checkerBrush;

        // Context menu items that need dynamic enable/disable
        private MenuItem _menuSave;
        private MenuItem _menuSaveAs;
        private MenuItem _menuDelete;
        private MenuItem _menuReload;
        private MenuItem _menuCopy;
        private MenuItem _menuProperties;
        private MenuItem _menuNext;
        private MenuItem _menuPrev;
        private MenuItem _menuRotateCw;
        private MenuItem _menuRotateCcw;
        private MenuItem _menuInvert;
        private MenuItem _menuCrop;
        private MenuItem _menuUndo;
        private MenuItem _menuAlwaysOnTop;
        private MenuItem _menuCheckerboard;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow(string initialFile = null)
        {
            Title = "Clean Image Viewer";
            Width = 1000;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;

            SourceInitialized += delegate
            {
                EnableDarkTitleBar(new WindowInteropHelper(this).Handle);
            };

            InitStyles();
            BuildUI();
            BuildContextMenu();
            HookEvents();

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
            {
                OpenImageFile(initialFile);
            }
            else
            {
                UpdateEmptyState();
            }
        }

        public static void EnableDarkTitleBar(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int useImmersiveDarkMode = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 (Windows 10 18985+ / Windows 11), 19 (Windows 10 1809-1909)
                int hr = DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        private void InitStyles()
        {
            _darkBrush = new SolidColorBrush(Color.FromRgb(24, 24, 24));
            _darkBrush.Freeze();

            // Checkerboard brush for transparent images
            DrawingGroup dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(36, 36, 36)), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(46, 46, 46)), null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(46, 46, 46)), null, new RectangleGeometry(new Rect(10, 10, 10, 10))));
            _checkerBrush = new DrawingBrush(dg)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 20, 20),
                ViewportUnits = BrushMappingMode.Absolute
            };
            _checkerBrush.Freeze();

            Background = _darkBrush;
        }

        private void BuildUI()
        {
            _rootGrid = new Grid();

            // Viewport container
            _viewportGrid = new Grid
            {
                ClipToBounds = true,
                Focusable = true,
                Background = Brushes.Transparent
            };

            _imageCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _imageControl = new Image
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            RenderOptions.SetBitmapScalingMode(_imageControl, BitmapScalingMode.HighQuality);

            _transformGroup = new TransformGroup();
            _scaleTransform = new ScaleTransform(1.0, 1.0);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
            _imageControl.RenderTransform = _transformGroup;

            _imageCanvas.Children.Add(_imageControl);
            _viewportGrid.Children.Add(_imageCanvas);
            _rootGrid.Children.Add(_viewportGrid);

            // Empty state placeholder
            _emptyStatePanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            TextBlock iconText = new TextBlock
            {
                Text = "🖼️",
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };

            TextBlock mainPrompt = new TextBlock
            {
                Text = "Right-click anywhere to Open an Image",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            TextBlock subPrompt = new TextBlock
            {
                Text = "or Drag and Drop image files directly here",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };

            TextBlock tipsPrompt = new TextBlock
            {
                Text = "All menus, edits, and tools are in the Right-Click Menu\nHotkeys: Arrow Keys (Next/Prev) | Wheel (Zoom) | Drag (Pan) | R (Rotate) | C (Crop) | I (Invert)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                LineHeight = 18
            };

            _emptyStatePanel.Children.Add(iconText);
            _emptyStatePanel.Children.Add(mainPrompt);
            _emptyStatePanel.Children.Add(subPrompt);
            _emptyStatePanel.Children.Add(tipsPrompt);
            _rootGrid.Children.Add(_emptyStatePanel);

            // Toast HUD overlay
            _hudBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18, 8, 18, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0),
                Opacity = 0.0,
                IsHitTestVisible = false
            };

            _hudText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _hudBorder.Child = _hudText;
            _rootGrid.Children.Add(_hudBorder);

            // Drag and drop support
            AllowDrop = true;
            Drop += MainWindow_Drop;
            DragEnter += MainWindow_DragEnter;

            Content = _rootGrid;
        }

        private void BuildContextMenu()
        {
            string menuStylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='MenuBg' Color='#202020'/>
  <SolidColorBrush x:Key='GutterBg' Color='#2F2F2F'/>
  <SolidColorBrush x:Key='GutterBorder' Color='#3C3C3C'/>
  <SolidColorBrush x:Key='MenuBorder' Color='#4A4A4A'/>
  <SolidColorBrush x:Key='ItemHover' Color='#383838'/>
  <SolidColorBrush x:Key='TextNormal' Color='#FFFFFF'/>
  <SolidColorBrush x:Key='TextDisabled' Color='#777777'/>
  <SolidColorBrush x:Key='TextShortcut' Color='#AAAAAA'/>
  <SolidColorBrush x:Key='SeparatorColor' Color='#383838'/>

  <Style TargetType='ContextMenu'>
    <Setter Property='Background' Value='{StaticResource MenuBg}'/>
    <Setter Property='BorderBrush' Value='{StaticResource MenuBorder}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='2'/>
    <Setter Property='Grid.IsSharedSizeScope' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}'
                  CornerRadius='4'
                  Padding='0'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='28'/>
                <ColumnDefinition Width='*'/>
              </Grid.ColumnDefinitions>
              <!-- Left vertical stripe / gutter: dark, but noticeably lighter than the #202020 menu background -->
              <Border Grid.Column='0'
                      Background='{StaticResource GutterBg}'
                      BorderBrush='{StaticResource GutterBorder}'
                      BorderThickness='0,0,1,0'
                      CornerRadius='4,0,0,4'/>
              <StackPanel Grid.ColumnSpan='2' IsItemsHost='True' Margin='1,3,1,3'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Grid Margin='0,2,0,2' SnapsToDevicePixels='True'>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='28'/>
              <ColumnDefinition Width='*'/>
            </Grid.ColumnDefinitions>
            <Rectangle Grid.Column='0' Fill='{StaticResource GutterBorder}' Height='1'/>
            <Rectangle Grid.Column='1' Fill='{StaticResource SeparatorColor}' Height='1'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='Foreground' Value='{StaticResource TextNormal}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='Height' Value='26'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='ItemBorder' Background='Transparent' CornerRadius='2' Margin='2,1,2,1'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='24' SharedSizeGroup='IconCol'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto' SharedSizeGroup='ShortcutCol'/>
              </Grid.ColumnDefinitions>

              <!-- Checkmark / Icon -->
              <ContentPresenter x:Name='IconHost' Grid.Column='0' ContentSource='Icon'
                                HorizontalAlignment='Center' VerticalAlignment='Center'/>
              <Path x:Name='CheckMark' Grid.Column='0'
                    Visibility='Collapsed'
                    Data='M 0 4 L 3 7 L 8 1'
                    Stroke='{StaticResource TextNormal}'
                    StrokeThickness='1.5'
                    HorizontalAlignment='Center'
                    VerticalAlignment='Center'/>

              <!-- Header Text -->
              <ContentPresenter Grid.Column='1'
                                ContentSource='Header'
                                RecognizesAccessKey='True'
                                VerticalAlignment='Center'
                                Margin='8,0,18,0'/>

              <!-- Shortcut Gesture -->
              <TextBlock Grid.Column='2'
                         Text='{TemplateBinding InputGestureText}'
                         Foreground='{StaticResource TextShortcut}'
                         VerticalAlignment='Center'
                         Margin='8,0,12,0'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='CheckMark' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='ItemBorder' Property='Background' Value='{StaticResource ItemHover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource TextDisabled}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

            ContextMenu cm = new ContextMenu();
            try
            {
                ResourceDictionary resDict = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(menuStylesXaml);
                cm.Resources.MergedDictionaries.Add(resDict);
            }
            catch { }

            // File Section
            MenuItem openItem = CreateMenuItem("Open File...", "Ctrl+O", delegate { PromptOpenFile(); });
            _menuSave = CreateMenuItem("Save", "Ctrl+S", delegate { SaveCurrentImage(); });
            _menuSaveAs = CreateMenuItem("Save As...", "Ctrl+Shift+S", delegate { SaveCurrentImageAs(); });
            _menuDelete = CreateMenuItem("Delete File", "Delete", delegate { DeleteCurrentImage(); });
            _menuReload = CreateMenuItem("Reload Original", "F5", delegate { ReloadOriginalImage(); });
            _menuCopy = CreateMenuItem("Copy Image to Clipboard", "Ctrl+C", delegate { CopyImageToClipboard(); });
            MenuItem openFolder = CreateMenuItem("Open Containing Folder", null, delegate { OpenContainingFolder(); });
            _menuProperties = CreateMenuItem("Image Properties / Info", "Alt+Enter", delegate { ShowPropertiesDialog(); });

            cm.Items.Add(openItem);
            cm.Items.Add(_menuSave);
            cm.Items.Add(_menuSaveAs);
            cm.Items.Add(_menuDelete);
            cm.Items.Add(_menuReload);
            cm.Items.Add(new Separator());
            cm.Items.Add(_menuCopy);
            cm.Items.Add(openFolder);
            cm.Items.Add(_menuProperties);
            cm.Items.Add(new Separator());

            // Navigation Section
            _menuNext = CreateMenuItem("Next Image", "Right Arrow / Space", delegate { NavigateNext(); });
            _menuPrev = CreateMenuItem("Previous Image", "Left Arrow / Backspace", delegate { NavigatePrevious(); });
            MenuItem firstItem = CreateMenuItem("First Image", "Home", delegate { NavigateFirst(); });
            MenuItem lastItem = CreateMenuItem("Last Image", "End", delegate { NavigateLast(); });

            cm.Items.Add(_menuNext);
            cm.Items.Add(_menuPrev);
            cm.Items.Add(firstItem);
            cm.Items.Add(lastItem);
            cm.Items.Add(new Separator());

            // Zoom & View Section
            MenuItem zoomInItem = CreateMenuItem("Zoom In", "Ctrl + Wheel Up / +", delegate { Zoom(1.25, new Point(ActualWidth / 2, ActualHeight / 2)); });
            MenuItem zoomOutItem = CreateMenuItem("Zoom Out", "Ctrl + Wheel Down / -", delegate { Zoom(1.0 / 1.25, new Point(ActualWidth / 2, ActualHeight / 2)); });
            MenuItem actualSizeItem = CreateMenuItem("Actual Size (100%)", "1 / Ctrl+0", delegate { SetActualSize(); });
            MenuItem fitWindowItem = CreateMenuItem("Fit to Window", "F / Ctrl+9", delegate { FitToWindow(); });

            cm.Items.Add(zoomInItem);
            cm.Items.Add(zoomOutItem);
            cm.Items.Add(actualSizeItem);
            cm.Items.Add(fitWindowItem);
            cm.Items.Add(new Separator());

            // Transform & Edit Section
            _menuRotateCw = CreateMenuItem("Rotate Clockwise 90°", "R", delegate { RotateImage(90); });
            _menuRotateCcw = CreateMenuItem("Rotate Counter-Clockwise 90°", "Shift+R", delegate { RotateImage(270); });
            MenuItem rotate180 = CreateMenuItem("Rotate 180°", null, delegate { RotateImage(180); });
            MenuItem flipH = CreateMenuItem("Flip Horizontally", "H", delegate { FlipImage(true, false); });
            MenuItem flipV = CreateMenuItem("Flip Vertically", "V", delegate { FlipImage(false, true); });
            _menuInvert = CreateMenuItem("Invert Colors", "I", delegate { InvertColors(); });
            _menuCrop = CreateMenuItem("Crop Image", "C", delegate { ToggleCropMode(); });
            _menuUndo = CreateMenuItem("Undo Edit", "Ctrl+Z", delegate { UndoEdit(); });

            cm.Items.Add(_menuRotateCw);
            cm.Items.Add(_menuRotateCcw);
            cm.Items.Add(rotate180);
            cm.Items.Add(flipH);
            cm.Items.Add(flipV);
            cm.Items.Add(new Separator());
            cm.Items.Add(_menuInvert);
            cm.Items.Add(_menuCrop);
            cm.Items.Add(_menuUndo);
            cm.Items.Add(new Separator());

            // Window / Preferences Section
            MenuItem fullscreenItem = CreateMenuItem("Toggle Fullscreen", "F11", delegate { ToggleFullscreen(); });

            _menuAlwaysOnTop = new MenuItem
            {
                Header = "Always on Top",
                IsCheckable = true,
                Foreground = Brushes.White
            };
            _menuAlwaysOnTop.Click += delegate
            {
                Topmost = _menuAlwaysOnTop.IsChecked;
                ShowHud(Topmost ? "Always on Top: ON" : "Always on Top: OFF");
            };

            _menuCheckerboard = new MenuItem
            {
                Header = "Transparency Checkerboard",
                IsCheckable = true,
                Foreground = Brushes.White
            };
            _menuCheckerboard.Click += delegate
            {
                _useCheckerboard = _menuCheckerboard.IsChecked;
                Background = _useCheckerboard ? _checkerBrush : _darkBrush;
                ShowHud(_useCheckerboard ? "Checkerboard: ON" : "Checkerboard: OFF");
            };

            MenuItem exitItem = CreateMenuItem("Exit", "Alt+F4", delegate { Close(); });

            cm.Items.Add(fullscreenItem);
            cm.Items.Add(_menuAlwaysOnTop);
            cm.Items.Add(_menuCheckerboard);
            cm.Items.Add(new Separator());
            cm.Items.Add(exitItem);

            ContextMenu = cm;

            // Before opening context menu, update enabled states
            ContextMenu.Opened += delegate
            {
                bool hasImage = _currentBitmap != null;
                bool hasFile = !_isZipMode && !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath);
                _menuSave.IsEnabled = hasImage && _isModified && !_isZipMode;
                _menuSaveAs.IsEnabled = hasImage;
                _menuDelete.IsEnabled = hasFile;
                _menuReload.IsEnabled = hasImage && _isModified;
                _menuCopy.IsEnabled = hasImage;
                _menuProperties.IsEnabled = hasImage;
                _menuNext.IsEnabled = _folderFiles.Count > 1;
                _menuPrev.IsEnabled = _folderFiles.Count > 1;
                _menuRotateCw.IsEnabled = hasImage;
                _menuRotateCcw.IsEnabled = hasImage;
                _menuInvert.IsEnabled = hasImage;
                _menuCrop.IsEnabled = hasImage;
                _menuUndo.IsEnabled = _undoStack.Count > 0;
            };
        }

        private MenuItem CreateMenuItem(string header, string gesture, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                InputGestureText = gesture ?? string.Empty,
                Foreground = Brushes.White
            };
            item.Click += handler;
            return item;
        }

        private void HookEvents()
        {
            _viewportGrid.MouseWheel += Viewport_MouseWheel;
            _viewportGrid.MouseDown += Viewport_MouseDown;
            _viewportGrid.MouseMove += Viewport_MouseMove;
            _viewportGrid.MouseUp += Viewport_MouseUp;
            _viewportGrid.SizeChanged += delegate { OnViewportSizeChanged(); };

            KeyDown += MainWindow_KeyDown;
        }

        private void OnViewportSizeChanged()
        {
            if (_cropOverlay != null)
            {
                _cropOverlay.UpdateViewportTransform(_scale, _offset);
            }
        }

        public void OpenImageFile(string path)
        {
            if (!File.Exists(path)) return;

            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                OpenZipArchive(path);
                return;
            }

            try
            {
                _isZipMode = false;
                _currentZipFilePath = null;
                _currentZipEntryName = null;
                _currentZipEntryBytes = null;

                BitmapSource bs = ImageHelper.LoadImage(path);
                _originalBitmap = bs;
                _currentBitmap = bs;
                _currentFilePath = path;
                _isModified = false;
                _undoStack.Clear();

                ExitCropMode();
                ScanFolder(path);

                _imageControl.Source = _currentBitmap;
                _imageControl.Width = _currentBitmap.PixelWidth;
                _imageControl.Height = _currentBitmap.PixelHeight;

                UpdateEmptyState();
                Dispatcher.BeginInvoke(new Action(delegate { FitToWindow(); }));

                string countInfo = _folderFiles.Count > 1 ? string.Format(" ({0} of {1})", _currentFileIndex + 1, _folderFiles.Count) : "";
                ShowHud(Path.GetFileName(path) + countInfo);
                UpdateTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open image: " + ex.Message, "Error Opening Image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenZipArchive(string zipPath, string targetEntryName = null)
        {
            if (!File.Exists(zipPath)) return;

            try
            {
                List<string> imageEntries = new List<string>();
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Length == 0 && string.IsNullOrEmpty(entry.Name)) continue; // Directory entry
                        string entryExt = Path.GetExtension(entry.FullName);
                        if (ImageHelper.IsSupportedExtension(entryExt))
                        {
                            imageEntries.Add(entry.FullName);
                        }
                    }
                }

                if (imageEntries.Count == 0)
                {
                    MessageBox.Show(this, "No supported image files found in zip archive: " + Path.GetFileName(zipPath), "Empty Archive", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                imageEntries.Sort(new NaturalStringComparer());

                int entryIndex = 0;
                if (!string.IsNullOrEmpty(targetEntryName))
                {
                    int found = imageEntries.FindIndex(delegate(string e) {
                        return string.Equals(e, targetEntryName, StringComparison.OrdinalIgnoreCase);
                    });
                    if (found >= 0) entryIndex = found;
                }

                _isZipMode = true;
                _currentZipFilePath = zipPath;
                _folderFiles = imageEntries;
                _currentFileIndex = entryIndex;

                LoadZipEntryAtIndex(entryIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read zip archive: " + ex.Message, "Zip Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadZipEntryAtIndex(int index)
        {
            if (!_isZipMode || string.IsNullOrEmpty(_currentZipFilePath) || !File.Exists(_currentZipFilePath)) return;
            if (index < 0 || index >= _folderFiles.Count) return;

            string entryName = _folderFiles[index];

            try
            {
                byte[] entryBytes = null;
                using (ZipArchive archive = ZipFile.OpenRead(_currentZipFilePath))
                {
                    ZipArchiveEntry entry = archive.GetEntry(entryName);
                    if (entry == null)
                    {
                        // Case-insensitive match fallback
                        foreach (ZipArchiveEntry e in archive.Entries)
                        {
                            if (string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase))
                            {
                                entry = e;
                                break;
                            }
                        }
                    }

                    if (entry == null)
                    {
                        throw new FileNotFoundException("Entry not found in zip archive: " + entryName);
                    }

                    using (Stream s = entry.Open())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        entryBytes = ms.ToArray();
                    }
                }

                string entryExt = Path.GetExtension(entryName);
                BitmapSource bs = ImageHelper.LoadImageFromBytes(entryBytes, entryExt);

                _originalBitmap = bs;
                _currentBitmap = bs;
                _currentFilePath = _currentZipFilePath + " > " + entryName;
                _currentZipEntryName = entryName;
                _currentZipEntryBytes = entryBytes;
                _currentFileIndex = index;
                _isModified = false;
                _undoStack.Clear();

                ExitCropMode();

                _imageControl.Source = _currentBitmap;
                _imageControl.Width = _currentBitmap.PixelWidth;
                _imageControl.Height = _currentBitmap.PixelHeight;

                UpdateEmptyState();
                Dispatcher.BeginInvoke(new Action(delegate { FitToWindow(); }));

                string countInfo = _folderFiles.Count > 1 ? string.Format(" ({0} of {1})", _currentFileIndex + 1, _folderFiles.Count) : "";
                string displayLabel = Path.GetFileName(entryName) + " [" + Path.GetFileName(_currentZipFilePath) + "]" + countInfo;
                ShowHud(displayLabel);
                UpdateTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load image from zip: " + ex.Message, "Zip Image Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScanFolder(string currentFile)
        {
            try
            {
                string dir = Path.GetDirectoryName(currentFile);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                string[] allFiles = Directory.GetFiles(dir);
                List<string> imageFiles = new List<string>();
                foreach (string file in allFiles)
                {
                    string ext = Path.GetExtension(file);
                    if (ImageHelper.IsSupportedExtension(ext))
                    {
                        imageFiles.Add(file);
                    }
                }

                imageFiles.Sort(new NaturalStringComparer());
                _folderFiles = imageFiles;
                _currentFileIndex = _folderFiles.FindIndex(delegate(string f) {
                    return string.Equals(f, currentFile, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch { }
        }

        private void UpdateEmptyState()
        {
            bool hasImage = _currentBitmap != null;
            _emptyStatePanel.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
            _imageControl.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTitle()
        {
            if (_currentBitmap == null)
            {
                Title = "Clean Image Viewer";
                return;
            }

            string fileName;
            if (_isZipMode)
            {
                fileName = Path.GetFileName(_currentZipEntryName) + " [" + Path.GetFileName(_currentZipFilePath) + "]";
            }
            else
            {
                fileName = !string.IsNullOrEmpty(_currentFilePath) ? Path.GetFileName(_currentFilePath) : "Untitled";
            }

            string modTag = _isModified ? "*" : "";
            int zoomPct = (int)Math.Round(_scale * 100.0);
            int w = _currentBitmap.PixelWidth;
            int h = _currentBitmap.PixelHeight;

            string folderIndex = "";
            if (_folderFiles.Count > 1 && _currentFileIndex >= 0)
            {
                folderIndex = string.Format(" [{0}/{1}]", _currentFileIndex + 1, _folderFiles.Count);
            }

            Title = string.Format("{0}{1}{2} ({3}×{4}) - {5}% - Clean Image Viewer", modTag, fileName, folderIndex, w, h, zoomPct);
        }

        private void UpdateTransforms()
        {
            _scaleTransform.ScaleX = _scale;
            _scaleTransform.ScaleY = _scale;
            _translateTransform.X = _offset.X;
            _translateTransform.Y = _offset.Y;

            // Pixel-art mode when zoomed way in (> 400%)
            if (_scale >= 4.0)
            {
                RenderOptions.SetBitmapScalingMode(_imageControl, BitmapScalingMode.NearestNeighbor);
            }
            else
            {
                RenderOptions.SetBitmapScalingMode(_imageControl, BitmapScalingMode.HighQuality);
            }

            if (_cropOverlay != null)
            {
                _cropOverlay.UpdateViewportTransform(_scale, _offset);
            }

            UpdateTitle();
        }

        private void Zoom(double factor, Point center)
        {
            if (_currentBitmap == null) return;

            double oldScale = _scale;
            double newScale = Math.Max(0.02, Math.Min(60.0, _scale * factor));

            _offset.X = center.X - (center.X - _offset.X) * (newScale / oldScale);
            _offset.Y = center.Y - (center.Y - _offset.Y) * (newScale / oldScale);
            _scale = newScale;

            UpdateTransforms();
            ShowHud(string.Format("Zoom: {0}%", (int)Math.Round(_scale * 100.0)));
        }

        public void FitToWindow()
        {
            if (_currentBitmap == null) return;

            double viewW = _viewportGrid.ActualWidth;
            double viewH = _viewportGrid.ActualHeight;
            if (viewW <= 0 || viewH <= 0)
            {
                viewW = ActualWidth - 16;
                viewH = ActualHeight - 40;
            }

            double imgW = _currentBitmap.PixelWidth;
            double imgH = _currentBitmap.PixelHeight;

            if (imgW <= 0 || imgH <= 0) return;

            double scaleX = viewW / imgW;
            double scaleY = viewH / imgH;
            _scale = Math.Min(scaleX, scaleY);
            if (_scale > 1.0) _scale = 1.0; // Don't upscale tiny icons automatically beyond 1:1

            _offset.X = (viewW - imgW * _scale) / 2.0;
            _offset.Y = (viewH - imgH * _scale) / 2.0;

            UpdateTransforms();
            ShowHud(string.Format("Fit: {0}%", (int)Math.Round(_scale * 100.0)));
        }

        public void SetActualSize()
        {
            if (_currentBitmap == null) return;

            double viewW = _viewportGrid.ActualWidth;
            double viewH = _viewportGrid.ActualHeight;
            double imgW = _currentBitmap.PixelWidth;
            double imgH = _currentBitmap.PixelHeight;

            _scale = 1.0;
            _offset.X = (viewW - imgW) / 2.0;
            _offset.Y = (viewH - imgH) / 2.0;

            UpdateTransforms();
            ShowHud("Actual Size: 100%");
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_currentBitmap == null) return;

            Point mousePos = e.GetPosition(_viewportGrid);
            double factor = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
            Zoom(factor, mousePos);
            e.Handled = true;
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                    return;
                }

                if (!_isCropMode && _currentBitmap != null)
                {
                    _isPanning = true;
                    _panStartMouse = e.GetPosition(_viewportGrid);
                    _panStartOffset = _offset;
                    _viewportGrid.CaptureMouse();
                    _viewportGrid.Cursor = Cursors.SizeAll;
                    e.Handled = true;
                }
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentMouse = e.GetPosition(_viewportGrid);
                _offset.X = _panStartOffset.X + (currentMouse.X - _panStartMouse.X);
                _offset.Y = _panStartOffset.Y + (currentMouse.Y - _panStartMouse.Y);
                UpdateTransforms();
                e.Handled = true;
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _isPanning)
            {
                _isPanning = false;
                _viewportGrid.ReleaseMouseCapture();
                _viewportGrid.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        // Navigation
        public void NavigateNext()
        {
            if (_folderFiles.Count == 0) return;
            if (!CheckUnsavedChangesPrompt()) return;

            _currentFileIndex = (_currentFileIndex + 1) % _folderFiles.Count;
            if (_isZipMode)
            {
                LoadZipEntryAtIndex(_currentFileIndex);
            }
            else
            {
                OpenImageFile(_folderFiles[_currentFileIndex]);
            }
        }

        public void NavigatePrevious()
        {
            if (_folderFiles.Count == 0) return;
            if (!CheckUnsavedChangesPrompt()) return;

            _currentFileIndex = (_currentFileIndex - 1 + _folderFiles.Count) % _folderFiles.Count;
            if (_isZipMode)
            {
                LoadZipEntryAtIndex(_currentFileIndex);
            }
            else
            {
                OpenImageFile(_folderFiles[_currentFileIndex]);
            }
        }

        public void NavigateFirst()
        {
            if (_folderFiles.Count == 0) return;
            if (!CheckUnsavedChangesPrompt()) return;

            _currentFileIndex = 0;
            if (_isZipMode)
            {
                LoadZipEntryAtIndex(_currentFileIndex);
            }
            else
            {
                OpenImageFile(_folderFiles[0]);
            }
        }

        public void NavigateLast()
        {
            if (_folderFiles.Count == 0) return;
            if (!CheckUnsavedChangesPrompt()) return;

            _currentFileIndex = _folderFiles.Count - 1;
            if (_isZipMode)
            {
                LoadZipEntryAtIndex(_currentFileIndex);
            }
            else
            {
                OpenImageFile(_folderFiles[_currentFileIndex]);
            }
        }

        // Transformations
        public void RotateImage(double angle)
        {
            if (_currentBitmap == null) return;
            PushUndo();

            _currentBitmap = ImageHelper.Rotate(_currentBitmap, angle);
            ApplyModifiedBitmap();
            ShowHud(string.Format("Rotated {0}°", angle));
        }

        public void FlipImage(bool horizontal, bool vertical)
        {
            if (_currentBitmap == null) return;
            PushUndo();

            _currentBitmap = ImageHelper.Flip(_currentBitmap, horizontal, vertical);
            ApplyModifiedBitmap();
            ShowHud(horizontal ? "Flipped Horizontally" : "Flipped Vertically");
        }

        public void InvertColors()
        {
            if (_currentBitmap == null) return;
            PushUndo();

            _currentBitmap = ImageHelper.InvertColors(_currentBitmap);
            ApplyModifiedBitmap();
            ShowHud("Colors Inverted");
        }

        // Crop Tool
        public void ToggleCropMode()
        {
            if (_currentBitmap == null) return;

            if (_isCropMode)
            {
                ExitCropMode();
            }
            else
            {
                EnterCropMode();
            }
        }

        private void EnterCropMode()
        {
            if (_currentBitmap == null) return;

            _isCropMode = true;
            if (_cropOverlay == null)
            {
                _cropOverlay = new CropOverlay();
                _cropOverlay.CropConfirmed += OnCropConfirmed;
                _cropOverlay.CropCancelled += OnCropCancelled;
                _rootGrid.Children.Add(_cropOverlay);
            }

            _cropOverlay.Visibility = Visibility.Visible;
            _cropOverlay.Initialize(_currentBitmap.PixelWidth, _currentBitmap.PixelHeight, _scale, _offset);
            ShowHud("Crop: Drag box. Enter to Apply, Esc to Cancel");
        }

        private void ExitCropMode()
        {
            _isCropMode = false;
            if (_cropOverlay != null)
            {
                _cropOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void OnCropConfirmed(Int32Rect cropRect)
        {
            if (_currentBitmap == null) return;

            PushUndo();
            _currentBitmap = ImageHelper.Crop(_currentBitmap, cropRect);
            ExitCropMode();
            ApplyModifiedBitmap();
            FitToWindow();
            ShowHud(string.Format("Cropped to {0} × {1} px", cropRect.Width, cropRect.Height));
        }

        private void OnCropCancelled()
        {
            ExitCropMode();
            ShowHud("Crop Cancelled");
        }

        // Undo
        private void PushUndo()
        {
            if (_currentBitmap != null)
            {
                _undoStack.Push(_currentBitmap);
            }
        }

        public void UndoEdit()
        {
            if (_undoStack.Count > 0)
            {
                _currentBitmap = _undoStack.Pop();
                _isModified = _undoStack.Count > 0;
                ApplyModifiedBitmap();
                ShowHud("Undo Applied");
            }
            else
            {
                ShowHud("Nothing to Undo");
            }
        }

        private void ApplyModifiedBitmap()
        {
            _isModified = true;
            _imageControl.Source = _currentBitmap;
            _imageControl.Width = _currentBitmap.PixelWidth;
            _imageControl.Height = _currentBitmap.PixelHeight;
            UpdateTitle();
        }

        // Save & Save As
        public void SaveCurrentImage()
        {
            if (_currentBitmap == null) return;

            if (_isZipMode || string.IsNullOrEmpty(_currentFilePath))
            {
                SaveCurrentImageAs();
                return;
            }

            try
            {
                ImageHelper.SaveImage(_currentBitmap, _currentFilePath);
                _isModified = false;
                _undoStack.Clear();
                _originalBitmap = _currentBitmap;
                UpdateTitle();
                ShowHud("Saved successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save image: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SaveCurrentImageAs()
        {
            if (_currentBitmap == null) return;

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif;*.tiff)|*.tif;*.tiff|GIF Image (*.gif)|*.gif|All Files (*.*)|*.*";
            dlg.FilterIndex = 1;

            if (_isZipMode)
            {
                dlg.InitialDirectory = !string.IsNullOrEmpty(_currentZipFilePath) ? Path.GetDirectoryName(_currentZipFilePath) : null;
                dlg.FileName = !string.IsNullOrEmpty(_currentZipEntryName) ? Path.GetFileName(_currentZipEntryName) : "image.png";
            }
            else if (!string.IsNullOrEmpty(_currentFilePath))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(_currentFilePath);
                dlg.FileName = Path.GetFileName(_currentFilePath);
            }
            else
            {
                dlg.FileName = "image.png";
            }

            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    ImageHelper.SaveImage(_currentBitmap, dlg.FileName);
                    if (!_isZipMode)
                    {
                        _currentFilePath = dlg.FileName;
                        _originalBitmap = _currentBitmap;
                        ScanFolder(_currentFilePath);
                    }
                    _isModified = false;
                    _undoStack.Clear();
                    UpdateTitle();
                    ShowHud("Saved as " + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save image: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void DeleteCurrentImage()
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath)) return;

            string fileName = Path.GetFileName(_currentFilePath);
            MessageBoxResult res = MessageBox.Show(this,
                string.Format("Are you sure you want to send \"{0}\" to the Recycle Bin?", fileName),
                "Delete File",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                string deletedPath = _currentFilePath;
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    deletedPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                ShowHud("Deleted: " + fileName);

                // Determine next image to show from folder list
                string nextToShow = null;
                if (_folderFiles != null && _folderFiles.Count > 1 && _currentFileIndex >= 0)
                {
                    if (_currentFileIndex + 1 < _folderFiles.Count)
                    {
                        nextToShow = _folderFiles[_currentFileIndex + 1];
                    }
                    else if (_currentFileIndex - 1 >= 0)
                    {
                        nextToShow = _folderFiles[_currentFileIndex - 1];
                    }
                }

                if (!string.IsNullOrEmpty(nextToShow) && File.Exists(nextToShow))
                {
                    OpenImageFile(nextToShow);
                }
                else
                {
                    // No other images left in folder
                    _currentBitmap = null;
                    _originalBitmap = null;
                    _currentFilePath = null;
                    _isModified = false;
                    _undoStack.Clear();
                    _folderFiles.Clear();
                    _currentFileIndex = -1;
                    _imageControl.Source = null;
                    ExitCropMode();
                    UpdateEmptyState();
                    UpdateTitle();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not delete file: " + ex.Message, "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ReloadOriginalImage()
        {
            if (_isZipMode && !string.IsNullOrEmpty(_currentZipFilePath))
            {
                LoadZipEntryAtIndex(_currentFileIndex);
                ShowHud("Reloaded image from zip");
            }
            else if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
            {
                OpenImageFile(_currentFilePath);
                ShowHud("Reloaded original image");
            }
            else if (_originalBitmap != null)
            {
                PushUndo();
                _currentBitmap = _originalBitmap;
                _isModified = false;
                ApplyModifiedBitmap();
                FitToWindow();
                ShowHud("Reverted to original");
            }
        }

        public void CopyImageToClipboard()
        {
            if (_currentBitmap == null) return;
            try
            {
                Clipboard.SetImage(_currentBitmap);
                ShowHud("Copied image to clipboard");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Clipboard error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void OpenContainingFolder()
        {
            string targetPath = _isZipMode ? _currentZipFilePath : _currentFilePath;
            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
            {
                try
                {
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", targetPath));
                }
                catch { }
            }
        }

        public void PromptOpenFile()
        {
            if (!CheckUnsavedChangesPrompt()) return;

            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "All Supported Files|*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.bmp;*.dib;*.gif;*.ico;*.tiff;*.tif;*.webp;*.svg;*.wmp;*.hdp;*.wdp;*.heic;*.avif;*.zip|Zip Archive (*.zip)|*.zip|SVG Vector Image (*.svg)|*.svg|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp|Icon (*.ico)|*.ico|TIFF (*.tif;*.tiff)|*.tif;*.tiff|GIF (*.gif)|*.gif|All Files (*.*)|*.*";

            if (dlg.ShowDialog(this) == true)
            {
                OpenImageFile(dlg.FileName);
            }
        }

        public void ShowPropertiesDialog()
        {
            if (_currentBitmap == null) return;
            PropertiesWindow pw;
            if (_isZipMode)
            {
                long entrySize = _currentZipEntryBytes != null ? _currentZipEntryBytes.Length : 0;
                pw = new PropertiesWindow(this, _currentZipFilePath, _currentZipEntryName, entrySize, _currentBitmap);
            }
            else
            {
                pw = new PropertiesWindow(this, _currentFilePath, _currentBitmap);
            }
            pw.ShowDialog();
        }

        private bool CheckUnsavedChangesPrompt()
        {
            if (_isModified && _currentBitmap != null)
            {
                string name = !string.IsNullOrEmpty(_currentFilePath) ? Path.GetFileName(_currentFilePath) : "current image";
                MessageBoxResult res = MessageBox.Show(this,
                    string.Format("Save changes to \"{0}\"?", name),
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    SaveCurrentImage();
                    return true;
                }
                else if (res == MessageBoxResult.No)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        // Fullscreen Toggle
        private WindowState _prevWindowState = WindowState.Normal;
        private WindowStyle _prevWindowStyle = WindowStyle.SingleBorderWindow;
        private bool _isFullscreen = false;

        public void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _prevWindowState = WindowState;
                _prevWindowStyle = WindowStyle;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                _isFullscreen = true;
                ShowHud("Fullscreen (F11 or Esc to exit)");
            }
            else
            {
                WindowStyle = _prevWindowStyle;
                WindowState = _prevWindowState;
                _isFullscreen = false;
                ShowHud("Exited Fullscreen");
            }
        }

        // Toast HUD
        public void ShowHud(string message)
        {
            _hudText.Text = message;

            if (_hudAnimation != null)
            {
                _hudAnimation.Stop();
            }

            DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
            DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(1300)
            };

            _hudAnimation = new Storyboard();
            Storyboard.SetTarget(_hudAnimation, _hudBorder);
            Storyboard.SetTargetProperty(_hudAnimation, new PropertyPath("Opacity"));

            _hudAnimation.Children.Add(fadeIn);
            _hudAnimation.Children.Add(fadeOut);
            _hudAnimation.Begin();
        }

        // Keyboard Shortcuts
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            // Crop Overlay handles its own Enter / Esc if focused, but if not:
            if (_isCropMode)
            {
                if (e.Key == Key.Enter)
                {
                    _cropOverlay.ApplyCrop();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                {
                    _cropOverlay.CancelCrop();
                    e.Handled = true;
                    return;
                }
            }

            switch (e.Key)
            {
                case Key.Delete:
                    DeleteCurrentImage();
                    e.Handled = true;
                    break;

                case Key.Right:
                case Key.PageDown:
                case Key.Space:
                    NavigateNext();
                    e.Handled = true;
                    break;

                case Key.Left:
                case Key.PageUp:
                case Key.Back:
                    NavigatePrevious();
                    e.Handled = true;
                    break;

                case Key.Home:
                    NavigateFirst();
                    e.Handled = true;
                    break;

                case Key.End:
                    NavigateLast();
                    e.Handled = true;
                    break;

                case Key.O:
                    if (ctrl) { PromptOpenFile(); e.Handled = true; }
                    break;

                case Key.S:
                    if (ctrl && shift) { SaveCurrentImageAs(); e.Handled = true; }
                    else if (ctrl) { SaveCurrentImage(); e.Handled = true; }
                    break;

                case Key.Z:
                    if (ctrl) { UndoEdit(); e.Handled = true; }
                    break;

                case Key.C:
                    if (ctrl) { CopyImageToClipboard(); e.Handled = true; }
                    else { ToggleCropMode(); e.Handled = true; }
                    break;

                case Key.R:
                    if (ctrl) { ReloadOriginalImage(); e.Handled = true; }
                    else if (shift) { RotateImage(270); e.Handled = true; }
                    else { RotateImage(90); e.Handled = true; }
                    break;

                case Key.L:
                    RotateImage(270);
                    e.Handled = true;
                    break;

                case Key.H:
                    FlipImage(true, false);
                    e.Handled = true;
                    break;

                case Key.V:
                    FlipImage(false, true);
                    e.Handled = true;
                    break;

                case Key.I:
                    InvertColors();
                    e.Handled = true;
                    break;

                case Key.F5:
                    ReloadOriginalImage();
                    e.Handled = true;
                    break;

                case Key.F:
                    FitToWindow();
                    e.Handled = true;
                    break;

                case Key.D0:
                case Key.NumPad0:
                    if (ctrl) { SetActualSize(); e.Handled = true; }
                    else { FitToWindow(); e.Handled = true; }
                    break;

                case Key.D1:
                case Key.NumPad1:
                    SetActualSize();
                    e.Handled = true;
                    break;

                case Key.OemPlus:
                case Key.Add:
                    Zoom(1.25, new Point(ActualWidth / 2, ActualHeight / 2));
                    e.Handled = true;
                    break;

                case Key.OemMinus:
                case Key.Subtract:
                    Zoom(1.0 / 1.25, new Point(ActualWidth / 2, ActualHeight / 2));
                    e.Handled = true;
                    break;

                case Key.F11:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (_isFullscreen) { ToggleFullscreen(); e.Handled = true; }
                    break;

                case Key.Return:
                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                    {
                        ShowPropertiesDialog();
                        e.Handled = true;
                    }
                    break;
            }
        }

        // Drag & Drop
        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    if (CheckUnsavedChangesPrompt())
                    {
                        OpenImageFile(files[0]);
                    }
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!CheckUnsavedChangesPrompt())
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }
    }
}
