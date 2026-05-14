using LetMeSee.Services;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace LetMeSee;

public partial class MainWindow : Window
{
    private const double WindowEdgeMargin = 80;
    private const double MinimumWindowWidth = 160;
    private const double MinimumWindowHeight = 120;
    private const double MinimumViewportWidth = 160;
    private const double MinimumViewportHeight = 120;
    private const double KeyboardPanStep = 80;
    private const double HiddenTitleBarResizeBorderThickness = 6;
    private const int ImageCachePreloadRadius = 2;
    private const int GifDisposalDoNotDispose = 1;
    private const int GifDisposalRestoreBackground = 2;
    private const int GifDisposalRestorePrevious = 3;

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff",
        // RAW formats — requires Windows Raw Image Extension (built-in on Windows 11)
        ".cr2",  // Canon
        ".cr3",  // Canon (newer)
        ".nef",  // Nikon
        ".arw",  // Sony
        ".raf",  // Fujifilm
        ".orf",  // Olympus
        ".rw2",  // Panasonic
        ".dng",  // Adobe DNG
        // HEIF — requires HEVC/HEIF extensions (built-in on Windows 11)
        ".heic",
        ".heif",
    };

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ImageLoader _imageLoader = new();
    private readonly LogicalPathComparer _pathComparer = new();
    private bool _hasLoadedFirstImage;
    private List<string> _folderImages = [];
    private BitmapSource? _currentImage;
    private AnimatedImage? _currentAnimation;
    private ImageSourceDetails? _currentSourceImageDetails;
    private string? _currentImagePath;
    private int _currentImageIndex = -1;
    private int _imageLoadVersion;
    private int _currentAnimationFrameIndex;
    private int _displayRotationDegrees;
    private double _imageWidth;
    private double _imageHeight;
    private double _zoomScale = 1;
    private double _imageOffsetX;
    private double _imageOffsetY;
    private ResizeMode _previousResizeMode;
    private Visibility _previousMenuVisibility;
    private Rect _previousWindowBounds;
    private bool _previousTopmost;
    private Point _dragStartPoint;
    private bool _isDragPending;
    private bool _isDeletingCurrentImage;
    private bool _isFitMode = true;
    private bool _isFullScreen;
    private bool _isImageInfoVisible;
    private bool _isWindowedTitleBarHidden;
    private DispatcherTimer? _animationTimer;

    public MainWindow(string? imagePath)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            UpdateFileAssociationsMenuItem();
            Viewport.Focus();
            await OpenInitialImageAsync(imagePath);
        };
    }

    private async Task OpenInitialImageAsync(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            MessageText.Text = "";
            return;
        }

        await LoadImageAsync(imagePath, enterFullScreen: _settings.StartFullScreen);
    }

    private async Task OpenFileFromDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.cr2;*.cr3;*.nef;*.arw;*.raf;*.orf;*.rw2;*.dng;*.heic;*.heif|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await LoadImageAsync(dialog.FileName);
        }
    }

    private async Task LoadImageAsync(
        string imagePath,
        bool refreshFolderImages = true,
        bool enterFullScreen = true,
        bool showLoadingMessage = true,
        bool resizeWindow = true)
    {
        imagePath = Path.GetFullPath(imagePath);
        var loadVersion = ++_imageLoadVersion;
        StopImageAnimation();
        _displayRotationDegrees = 0;

        if (refreshFolderImages)
        {
            RefreshFolderImages(imagePath);
        }

        if (showLoadingMessage && _currentImage is null)
        {
            MessageText.Text = "Loading...";
        }

        UpdateWindowTitle(imagePath, image: null);

        try
        {
            var image = await _imageLoader.LoadAsync(imagePath);
            var animation = await TryLoadAnimatedGifAsync(imagePath);
            if (loadVersion != _imageLoadVersion)
            {
                return;
            }

            _currentAnimation = animation;
            _currentAnimationFrameIndex = 0;
            _currentImage = animation?.Frames[0].Image ?? image;
            _currentImagePath = imagePath;
            _currentSourceImageDetails = ReadImageSourceDetails(imagePath);
            SetImageDisplaySize(_currentImage);
            ImageView.Source = _currentImage;
            UpdateWindowTitle(imagePath, _currentImage);
            UpdateImageInfoOverlay();
            MessageText.Text = "";

            if (enterFullScreen && !_isFullScreen)
            {
                ToggleFullScreen();
                UpdateLayout();
            }

            if (resizeWindow)
            {
                ResizeWindowForImage(keepWindowPosition: _hasLoadedFirstImage);
                _hasLoadedFirstImage = true;
            }

            FitToWindow();
            StartImageAnimation();
            QueueNearbyImagesForCache();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            if (loadVersion != _imageLoadVersion)
            {
                return;
            }

            ImageView.Source = null;
            _currentImage = null;
            _currentAnimation = null;
            _currentSourceImageDetails = null;
            _currentImagePath = null;
            _currentAnimationFrameIndex = 0;
            _displayRotationDegrees = 0;
            _imageWidth = 0;
            _imageHeight = 0;
            ResetImageOffset();
            ResetMinimumWindowSize();
            UpdateImageInfoOverlay();
            MessageText.Text = $"Cannot open image:{Environment.NewLine}{imagePath}{Environment.NewLine}{ex.Message}";
        }
    }

    private void RefreshFolderImages(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            _folderImages = [];
            _currentImageIndex = -1;
            return;
        }

        _folderImages = EnumerateFolderImages(imagePath);
        _currentImageIndex = _folderImages.FindIndex(path => string.Equals(path, imagePath, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> EnumerateFolderImages(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory)
            .Where(IsSupportedImageFile)
            .OrderBy(path => path, _pathComparer)
            .ToList();
    }

    private static bool IsSupportedImageFile(string path)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(path));
    }

    private async Task NavigateRelativeAsync(int delta)
    {
        if (_currentImagePath is not null)
        {
            RefreshFolderImages(_currentImagePath);
        }

        if (_folderImages.Count == 0 || _currentImageIndex < 0)
        {
            return;
        }

        await NavigateToIndexAsync(Math.Clamp(_currentImageIndex + delta, 0, _folderImages.Count - 1));
    }

    private async Task NavigateToIndexAsync(int index)
    {
        if (index < 0 || index >= _folderImages.Count || index == _currentImageIndex)
        {
            return;
        }

        _currentImageIndex = index;
        await LoadImageAsync(
            _folderImages[index],
            refreshFolderImages: false,
            enterFullScreen: false,
            showLoadingMessage: false);
    }

    private void QueueNearbyImagesForCache()
    {
        if (_folderImages.Count == 0 || _currentImageIndex < 0)
        {
            return;
        }

        var imagePaths = new List<string>();
        for (var distance = 1; distance <= ImageCachePreloadRadius; distance++)
        {
            var nextIndex = _currentImageIndex + distance;
            if (nextIndex < _folderImages.Count)
            {
                imagePaths.Add(_folderImages[nextIndex]);
            }

            var previousIndex = _currentImageIndex - distance;
            if (previousIndex >= 0)
            {
                imagePaths.Add(_folderImages[previousIndex]);
            }
        }

        if (imagePaths.Count > 0)
        {
            _ = PreloadImagesAsync(imagePaths);
        }
    }

    private async Task PreloadImagesAsync(IReadOnlyCollection<string> imagePaths)
    {
        try
        {
            await _imageLoader.PreloadAsync(imagePaths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            // Cache preloading is best-effort; visible image loading reports its own failures.
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomAtViewportCenter(1.15);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomAtViewportCenter(1 / 1.15);
                e.Handled = true;
                break;
            case Key.Divide:
            case Key.OemQuestion:
                ActualSize();
                e.Handled = true;
                break;
            case Key.D1 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad1 when Keyboard.Modifiers == ModifierKeys.None:
                ActualSize();
                e.Handled = true;
                break;
            case Key.D2 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad2 when Keyboard.Modifiers == ModifierKeys.None:
                SetFixedScale(2);
                e.Handled = true;
                break;
            case Key.D3 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad3 when Keyboard.Modifiers == ModifierKeys.None:
                SetFixedScale(3);
                e.Handled = true;
                break;
            case Key.Multiply:
                FitToWindow();
                e.Handled = true;
                break;
            case Key.D8 when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                FitToWindow();
                e.Handled = true;
                break;
            case Key.Left:
                if (!PanImageWithKeyboard(horizontalDirection: -1, verticalDirection: 0))
                {
                    _ = NavigateRelativeAsync(-1);
                }

                e.Handled = true;
                break;
            case Key.Up:
                if (!PanImageWithKeyboard(horizontalDirection: 0, verticalDirection: -1))
                {
                    _ = NavigateRelativeAsync(-1);
                }

                e.Handled = true;
                break;
            case Key.PageUp:
                _ = NavigateRelativeAsync(-1);
                e.Handled = true;
                break;
            case Key.Right:
                if (!PanImageWithKeyboard(horizontalDirection: 1, verticalDirection: 0))
                {
                    _ = NavigateRelativeAsync(1);
                }

                e.Handled = true;
                break;
            case Key.Down:
                if (!PanImageWithKeyboard(horizontalDirection: 0, verticalDirection: 1))
                {
                    _ = NavigateRelativeAsync(1);
                }

                e.Handled = true;
                break;
            case Key.PageDown:
                _ = NavigateRelativeAsync(1);
                e.Handled = true;
                break;
            case Key.Home:
                _ = NavigateToIndexAsync(0);
                e.Handled = true;
                break;
            case Key.End:
                _ = NavigateToIndexAsync(_folderImages.Count - 1);
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.Enter:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.None:
                ToggleImageInfoOverlay();
                e.Handled = true;
                break;
            case Key.O when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                _ = OpenFileFromDialogAsync();
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                CopyCurrentImageFileToClipboard();
                e.Handled = true;
                break;
            case Key.Delete:
                _ = DeleteCurrentImageAsync();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isFullScreen)
                {
                    Close();
                }
                else if (MainMenu.Visibility == Visibility.Visible)
                {
                    MainMenu.Visibility = Visibility.Collapsed;
                }
                else
                {
                    Close();
                }

                e.Handled = true;
                break;
        }
    }

    private void CopyCurrentImageFileToClipboard()
    {
        if (string.IsNullOrWhiteSpace(_currentImagePath) || !File.Exists(_currentImagePath))
        {
            return;
        }

        try
        {
            var fileDropList = new StringCollection
            {
                _currentImagePath
            };
            var dataObject = new DataObject();
            dataObject.SetFileDropList(fileDropList);
            dataObject.SetData(
                "Preferred DropEffect",
                new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
            Clipboard.SetDataObject(dataObject, true);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"複製圖片檔案失敗：{Environment.NewLine}{ex.Message}",
                "LetMeSee",
                MessageBoxButton.OK,
            MessageBoxImage.Error);
        }
    }

    private void SaveCurrentImageAs()
    {
        if (_currentImage is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save image as",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg;*.jpeg|Bitmap Image|*.bmp|GIF Image|*.gif|TIFF Image|*.tif;*.tiff",
            FileName = GetDefaultSaveAsFileName(),
            DefaultExt = ".png",
            AddExtension = false,
            OverwritePrompt = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var filePath = GetSaveAsFilePath(dialog);
        if (File.Exists(filePath))
        {
            var result = MessageBox.Show(
                this,
                $"檔案已存在，要取代它嗎？{Environment.NewLine}{filePath}",
                "LetMeSee",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            SaveBitmapSource(_currentImage, filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"另存圖片失敗：{Environment.NewLine}{ex.Message}",
                "LetMeSee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetDefaultSaveAsFileName()
    {
        var sourceName = string.IsNullOrWhiteSpace(_currentImagePath)
            ? "image"
            : Path.GetFileNameWithoutExtension(_currentImagePath);

        return $"{sourceName}_copy.png";
    }

    private static string GetSaveAsFilePath(SaveFileDialog dialog)
    {
        if (!string.IsNullOrWhiteSpace(Path.GetExtension(dialog.FileName)))
        {
            return dialog.FileName;
        }

        return Path.ChangeExtension(dialog.FileName, GetDefaultExtensionForFilterIndex(dialog.FilterIndex));
    }

    private static string GetDefaultExtensionForFilterIndex(int filterIndex)
    {
        return filterIndex switch
        {
            2 => ".jpg",
            3 => ".bmp",
            4 => ".gif",
            5 => ".tif",
            _ => ".png"
        };
    }

    private static void SaveBitmapSource(BitmapSource image, string filePath)
    {
        var encoder = CreateBitmapEncoder(filePath);
        encoder.Frames.Add(BitmapFrame.Create(PrepareImageForEncoder(image, encoder)));

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static BitmapEncoder CreateBitmapEncoder(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 100 },
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    private static BitmapSource PrepareImageForEncoder(BitmapSource image, BitmapEncoder encoder)
    {
        if (encoder is JpegBitmapEncoder && image.Format != PixelFormats.Bgr24)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            converted.Freeze();
            return converted;
        }

        return image;
    }

    private async Task DeleteCurrentImageAsync()
    {
        if (_isDeletingCurrentImage || string.IsNullOrWhiteSpace(_currentImagePath))
        {
            return;
        }

        _isDeletingCurrentImage = true;

        try
        {
            var imagePath = _currentImagePath;
            var folderImagesBeforeDelete = EnumerateFolderImages(imagePath);
            var deletedIndex = folderImagesBeforeDelete.FindIndex(path => string.Equals(path, imagePath, StringComparison.OrdinalIgnoreCase));

            if (File.Exists(imagePath))
            {
                FileSystem.DeleteFile(imagePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }

            var replacementImagePath = GetReplacementImagePathAfterDelete(
                folderImagesBeforeDelete,
                imagePath,
                deletedIndex);

            if (replacementImagePath is null)
            {
                ClearCurrentImageState();
                return;
            }

            await LoadImageAsync(
                replacementImagePath,
                refreshFolderImages: true,
                enterFullScreen: false,
                showLoadingMessage: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ExternalException)
        {
            MessageBox.Show(
                this,
                $"刪除圖片檔案失敗：{Environment.NewLine}{ex.Message}",
                "LetMeSee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isDeletingCurrentImage = false;
        }
    }

    private static string? GetReplacementImagePathAfterDelete(
        IReadOnlyList<string> folderImagesBeforeDelete,
        string deletedImagePath,
        int deletedIndex)
    {
        var remainingImages = folderImagesBeforeDelete
            .Where(path => !string.Equals(path, deletedImagePath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .ToList();

        if (remainingImages.Count == 0)
        {
            return null;
        }

        if (deletedIndex < 0)
        {
            return remainingImages[0];
        }

        return remainingImages.Skip(deletedIndex).FirstOrDefault()
            ?? remainingImages.Take(deletedIndex).LastOrDefault();
    }

    private void ClearCurrentImageState()
    {
        _imageLoadVersion++;
        StopImageAnimation();
        ImageView.Source = null;
        ImageView.Width = 0;
        ImageView.Height = 0;
        _currentImage = null;
        _currentAnimation = null;
        _currentSourceImageDetails = null;
        _currentImagePath = null;
        _currentImageIndex = -1;
        _folderImages = [];
        _currentAnimationFrameIndex = 0;
        _displayRotationDegrees = 0;
        _imageWidth = 0;
        _imageHeight = 0;
        _zoomScale = 1;
        _isFitMode = true;
        Title = "LetMeSee";
        MessageText.Text = "";
        ResetImageOffset();
        ResetMinimumWindowSize();
        UpdateImageInfoOverlay();

        if (_isFullScreen)
        {
            ToggleFullScreen();
        }
        else
        {
            SetWindowedTitleBarVisible(true);
        }
    }

    private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await OpenFileFromDialogAsync();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ActualSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ActualSize();
    }

    private void FitToWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        FitToWindow();
    }

    private void ViewportContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasCurrentImage = _currentImage is not null;
        SaveAsContextMenuItem.IsEnabled = hasCurrentImage;
        RotateLeftContextMenuItem.IsEnabled = hasCurrentImage;
        RotateRightContextMenuItem.IsEnabled = hasCurrentImage;
    }

    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentImageAs();
    }

    private void RotateLeftMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RotateCurrentImage(-90);
    }

    private void RotateRightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RotateCurrentImage(90);
    }

    private void FullScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullScreen();
    }

    private void FileAssociationsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var shouldRegister = FileAssociationsMenuItem.IsChecked;

        try
        {
            if (shouldRegister)
            {
                FileAssociationRegistrar.Register();
            }
            else
            {
                FileAssociationRegistrar.Unregister();
            }

            UpdateFileAssociationsMenuItem();
            MessageBox.Show(
                this,
                shouldRegister
                    ? "檔案關聯已完成。若右鍵選單沒有立刻更新，請重新開啟檔案總管或登出再登入。"
                    : "檔案關聯已取消。若右鍵選單沒有立刻更新，請重新開啟檔案總管或登出再登入。",
                "LetMeSee",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            UpdateFileAssociationsMenuItem();
            MessageBox.Show(
                this,
                $"檔案關聯更新失敗：{Environment.NewLine}{ex.Message}",
                "LetMeSee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var supportedFormats = string.Join(", ", SupportedImageExtensions.Order(StringComparer.OrdinalIgnoreCase));
        var message = string.Join(
            Environment.NewLine,
            $"LetMeSee {GetApplicationVersion()}",
            "",
            "功能：",
            "開啟圖片後自動全螢幕顯示。",
            "同資料夾圖片可用方向鍵或 PageUp/PageDown 切換。",
            "支援滑鼠滾輪縮放，圖片超出可視範圍時可用方向鍵平移。",
            "支援 GIF 動畫播放。",
            "F 或 Enter 可切換全螢幕。",
            "按 V 可在左下角顯示或隱藏目前圖片詳細資訊。",
            "右鍵選單可將目前圖片另存新檔。",
            "右鍵選單可將目前圖片左轉或右轉 90 度。",
            "Ctrl+C 可複製目前圖片檔案，Delete 可刪除目前圖片並切換下一張。",
            "視窗模式下雙擊圖片可隱藏或顯示標題列。",
            "可切換目前使用者的 Open with 與圖片右鍵選單關聯。",
            "",
            "支援格式：",
            supportedFormats);

        MessageBox.Show(
            this,
            message,
            "About LetMeSee",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateFileAssociationsMenuItem()
    {
        try
        {
            FileAssociationsMenuItem.IsChecked = FileAssociationRegistrar.IsRegistered();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FileAssociationsMenuItem.IsChecked = false;
        }
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex >= 0
                ? informationalVersion[..metadataIndex]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await LoadImageAsync(files[0]);
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentImage is null)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomAt(e.GetPosition(Viewport), e.Delta > 0 ? 1.15 : 1 / 1.15);
        }
        else
        {
            _ = NavigateRelativeAsync(e.Delta > 0 ? -1 : 1);
        }
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Viewport.Focus();

        if (e.ClickCount == 2)
        {
            if (_currentImage is not null && !_isFullScreen)
            {
                ToggleWindowedTitleBarVisibility();
            }
            else
            {
                ToggleMainMenuVisibility();
            }

            e.Handled = true;
            return;
        }

        if (_isFullScreen || WindowState == WindowState.Maximized)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _isDragPending = true;
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragPending || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragPending = false;
        Viewport.ReleaseMouseCapture();

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button state changes during startup or activation.
        }

        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragPending)
        {
            return;
        }

        _isDragPending = false;
        Viewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ToggleMainMenuVisibility()
    {
        if (MainMenu.Visibility != Visibility.Visible)
        {
            UpdateFileAssociationsMenuItem();
        }

        MainMenu.Visibility = MainMenu.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isFitMode)
        {
            FitToWindow();
        }
        else
        {
            ClampImageOffset();
        }
    }

    private void FitToWindow()
    {
        if (_currentImage is null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(Viewport.ActualWidth / _imageWidth, Viewport.ActualHeight / _imageHeight);
        ShowScaledImage(Math.Max(0.01, scale), stretchUniform: true);
        _isFitMode = true;
    }

    private void RotateCurrentImage(double angle)
    {
        if (_currentImage is null)
        {
            return;
        }

        if (_currentAnimation is not null)
        {
            _displayRotationDegrees = NormalizeRotationDegrees(_displayRotationDegrees + (int)angle);
            ShowAnimationFrame(_currentAnimationFrameIndex);
            ResizeAfterImageDimensionsChanged();
            return;
        }

        var rotatedImage = new TransformedBitmap(_currentImage, new RotateTransform(angle));
        rotatedImage.Freeze();

        _currentImage = rotatedImage;
        ImageView.Source = rotatedImage;
        _displayRotationDegrees = NormalizeRotationDegrees(_displayRotationDegrees + (int)angle);
        SetImageDisplaySize(rotatedImage);
        UpdateCurrentImageTitle();
        UpdateImageInfoOverlay();

        ResizeAfterImageDimensionsChanged();
    }

    private void ResizeAfterImageDimensionsChanged()
    {
        if (_isFitMode)
        {
            ResizeWindowForImage(keepWindowPosition: true);
            FitToWindow();
            return;
        }

        ShowScaledImage(_zoomScale, stretchUniform: false);
        ResetImageOffset();
    }

    private void StartImageAnimation()
    {
        if (_currentAnimation is null || _currentAnimation.Frames.Count <= 1)
        {
            return;
        }

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = _currentAnimation.Frames[0].Delay
        };
        _animationTimer.Tick += AnimationTimer_Tick;
        _animationTimer.Start();
    }

    private void StopImageAnimation()
    {
        if (_animationTimer is not null)
        {
            _animationTimer.Stop();
            _animationTimer.Tick -= AnimationTimer_Tick;
            _animationTimer = null;
        }

        _currentAnimation = null;
        _currentAnimationFrameIndex = 0;
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentAnimation is null || _currentAnimation.Frames.Count == 0)
        {
            StopImageAnimation();
            return;
        }

        _currentAnimationFrameIndex = (_currentAnimationFrameIndex + 1) % _currentAnimation.Frames.Count;
        ShowAnimationFrame(_currentAnimationFrameIndex, updateLayoutForSizeChange: true);

        if (_animationTimer is not null)
        {
            _animationTimer.Interval = _currentAnimation.Frames[_currentAnimationFrameIndex].Delay;
        }
    }

    private void ShowAnimationFrame(int frameIndex, bool updateLayoutForSizeChange = false)
    {
        if (_currentAnimation is null || frameIndex < 0 || frameIndex >= _currentAnimation.Frames.Count)
        {
            return;
        }

        var frame = ApplyDisplayRotation(_currentAnimation.Frames[frameIndex].Image);
        var sizeChanged = _currentImage is null ||
            _currentImage.PixelWidth != frame.PixelWidth ||
            _currentImage.PixelHeight != frame.PixelHeight ||
            Math.Abs(_currentImage.DpiX - frame.DpiX) > 0.01 ||
            Math.Abs(_currentImage.DpiY - frame.DpiY) > 0.01;

        _currentImage = frame;
        ImageView.Source = frame;
        SetImageDisplaySize(frame);

        if (updateLayoutForSizeChange && sizeChanged)
        {
            if (_isFitMode)
            {
                FitToWindow();
            }
            else
            {
                ShowScaledImage(_zoomScale, stretchUniform: false);
            }
        }

        UpdateCurrentImageTitle();
        UpdateImageInfoOverlay();
    }

    private BitmapSource ApplyDisplayRotation(BitmapSource image)
    {
        if (_displayRotationDegrees == 0)
        {
            return image;
        }

        var rotatedImage = new TransformedBitmap(image, new RotateTransform(_displayRotationDegrees));
        rotatedImage.Freeze();
        return rotatedImage;
    }

    private static int NormalizeRotationDegrees(int angle)
    {
        angle %= 360;

        if (angle > 180)
        {
            angle -= 360;
        }
        else if (angle <= -180)
        {
            angle += 360;
        }

        return angle;
    }

    private void ToggleImageInfoOverlay()
    {
        _isImageInfoVisible = !_isImageInfoVisible;
        UpdateImageInfoOverlay();
    }

    private void UpdateImageInfoOverlay()
    {
        if (!_isImageInfoVisible || _currentImage is null)
        {
            ImageInfoText.Visibility = Visibility.Collapsed;
            ImageInfoText.Text = "";
            return;
        }

        ImageInfoText.Text = BuildImageInfoText();
        ImageInfoText.Visibility = Visibility.Visible;
    }

    private string BuildImageInfoText()
    {
        if (_currentImage is null)
        {
            return "";
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_currentImagePath))
        {
            lines.Add($"檔案：{Path.GetFileName(_currentImagePath)}");
            lines.Add($"路徑：{_currentImagePath}");

            var fileInfo = new FileInfo(_currentImagePath);
            if (fileInfo.Exists)
            {
                lines.Add($"檔案大小：{FormatByteSize(fileInfo.Length)}");
            }
        }

        if (_currentSourceImageDetails is not null)
        {
            lines.Add($"來源解析度：{_currentSourceImageDetails.PixelWidth} x {_currentSourceImageDetails.PixelHeight}");
            lines.Add($"來源格式：{_currentSourceImageDetails.PixelFormat} ({_currentSourceImageDetails.BitsPerPixel} bpp)");
            lines.Add($"來源 DPI：{FormatDpi(_currentSourceImageDetails.DpiX)} x {FormatDpi(_currentSourceImageDetails.DpiY)}");
            lines.Add($"來源影格數：{_currentSourceImageDetails.FrameCount}");
            lines.Add($"內嵌 ICC/Profile：{(_currentSourceImageDetails.HasColorProfile ? "有" : "未偵測到")}");
        }
        else
        {
            lines.Add("來源格式：無法讀取");
        }

        lines.Add($"目前解析度：{_currentImage.PixelWidth} x {_currentImage.PixelHeight}");
        lines.Add($"目前格式：{_currentImage.Format} ({_currentImage.Format.BitsPerPixel} bpp)");
        lines.Add($"目前 DPI：{FormatDpi(_currentImage.DpiX)} x {FormatDpi(_currentImage.DpiY)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static Task<AnimatedImage?> TryLoadAnimatedGifAsync(string imagePath)
    {
        if (!string.Equals(Path.GetExtension(imagePath), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<AnimatedImage?>(null);
        }

        try
        {
            return Task.FromResult(LoadAnimatedGif(imagePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException or ArgumentException)
        {
            return Task.FromResult<AnimatedImage?>(null);
        }
    }

    private static AnimatedImage? LoadAnimatedGif(string imagePath)
    {
        using var stream = new FileStream(
            imagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 128 * 1024);
        var decoder = new GifBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count <= 1)
        {
            return null;
        }

        var frameMetadata = decoder.Frames.Select(GetGifFrameMetadata).ToList();
        var canvasWidth = GetGifLogicalScreenDimension(decoder.Metadata, "/logscrdesc/Width")
            ?? Math.Max(1, frameMetadata.Max(frame => frame.Left + frame.Width));
        var canvasHeight = GetGifLogicalScreenDimension(decoder.Metadata, "/logscrdesc/Height")
            ?? Math.Max(1, frameMetadata.Max(frame => frame.Top + frame.Height));

        var frames = new List<AnimatedImageFrame>(decoder.Frames.Count);
        BitmapSource? canvas = null;

        for (var i = 0; i < decoder.Frames.Count; i++)
        {
            var rawFrame = decoder.Frames[i];
            var metadata = frameMetadata[i];
            var previousCanvas = metadata.DisposalMethod == GifDisposalRestorePrevious
                ? canvas
                : null;
            var displayFrame = ComposeGifFrame(canvas, rawFrame, metadata, canvasWidth, canvasHeight);

            frames.Add(new AnimatedImageFrame(displayFrame, metadata.Delay));

            canvas = metadata.DisposalMethod switch
            {
                GifDisposalRestoreBackground => ClearGifFrameArea(displayFrame, metadata, canvasWidth, canvasHeight),
                GifDisposalRestorePrevious when previousCanvas is not null => previousCanvas,
                _ => displayFrame
            };
        }

        return new AnimatedImage(frames);
    }

    private static GifFrameMetadata GetGifFrameMetadata(BitmapFrame frame)
    {
        var metadata = frame.Metadata as BitmapMetadata;
        var left = GetBitmapMetadataInt(metadata, "/imgdesc/Left") ?? 0;
        var top = GetBitmapMetadataInt(metadata, "/imgdesc/Top") ?? 0;
        var width = GetBitmapMetadataInt(metadata, "/imgdesc/Width") ?? frame.PixelWidth;
        var height = GetBitmapMetadataInt(metadata, "/imgdesc/Height") ?? frame.PixelHeight;
        var disposalMethod = GetBitmapMetadataInt(metadata, "/grctlext/Disposal") ?? GifDisposalDoNotDispose;

        return new GifFrameMetadata(
            Math.Max(0, left),
            Math.Max(0, top),
            Math.Max(1, width),
            Math.Max(1, height),
            disposalMethod,
            GetGifFrameDelay(frame));
    }

    private static int? GetGifLogicalScreenDimension(BitmapMetadata? metadata, string query)
    {
        var dimension = GetBitmapMetadataInt(metadata, query);
        return dimension is > 0 ? dimension.Value : null;
    }

    private static int? GetBitmapMetadataInt(BitmapMetadata? metadata, string query)
    {
        try
        {
            if (metadata is not null &&
                metadata.ContainsQuery(query) &&
                metadata.GetQuery(query) is { } value)
            {
                return Convert.ToInt32(value);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FormatException or OverflowException)
        {
        }

        return null;
    }

    private static BitmapSource ComposeGifFrame(
        BitmapSource? canvas,
        BitmapSource frame,
        GifFrameMetadata metadata,
        int canvasWidth,
        int canvasHeight)
    {
        var bitmap = new RenderTargetBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var drawingContext = visual.RenderOpen())
        {
            if (canvas is not null)
            {
                drawingContext.DrawImage(canvas, new Rect(0, 0, canvasWidth, canvasHeight));
            }

            var drawLeft = frame.PixelWidth == canvasWidth && frame.PixelHeight == canvasHeight
                ? 0
                : metadata.Left;
            var drawTop = frame.PixelWidth == canvasWidth && frame.PixelHeight == canvasHeight
                ? 0
                : metadata.Top;

            drawingContext.DrawImage(
                frame,
                new Rect(drawLeft, drawTop, frame.PixelWidth, frame.PixelHeight));
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource ClearGifFrameArea(
        BitmapSource displayFrame,
        GifFrameMetadata metadata,
        int canvasWidth,
        int canvasHeight)
    {
        var clearRect = new Int32Rect(
            Math.Clamp(metadata.Left, 0, canvasWidth),
            Math.Clamp(metadata.Top, 0, canvasHeight),
            Math.Min(metadata.Width, Math.Max(0, canvasWidth - metadata.Left)),
            Math.Min(metadata.Height, Math.Max(0, canvasHeight - metadata.Top)));

        if (clearRect.Width <= 0 || clearRect.Height <= 0)
        {
            return displayFrame;
        }

        var bitmap = new RenderTargetBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var drawingContext = visual.RenderOpen())
        {
            DrawBitmapSegment(
                drawingContext,
                displayFrame,
                new Int32Rect(0, 0, canvasWidth, clearRect.Y),
                new Rect(0, 0, canvasWidth, clearRect.Y));
            DrawBitmapSegment(
                drawingContext,
                displayFrame,
                new Int32Rect(0, clearRect.Y, clearRect.X, clearRect.Height),
                new Rect(0, clearRect.Y, clearRect.X, clearRect.Height));
            DrawBitmapSegment(
                drawingContext,
                displayFrame,
                new Int32Rect(clearRect.X + clearRect.Width, clearRect.Y, canvasWidth - clearRect.X - clearRect.Width, clearRect.Height),
                new Rect(clearRect.X + clearRect.Width, clearRect.Y, canvasWidth - clearRect.X - clearRect.Width, clearRect.Height));
            DrawBitmapSegment(
                drawingContext,
                displayFrame,
                new Int32Rect(0, clearRect.Y + clearRect.Height, canvasWidth, canvasHeight - clearRect.Y - clearRect.Height),
                new Rect(0, clearRect.Y + clearRect.Height, canvasWidth, canvasHeight - clearRect.Y - clearRect.Height));
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawBitmapSegment(
        DrawingContext drawingContext,
        BitmapSource source,
        Int32Rect sourceRect,
        Rect destinationRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            return;
        }

        var croppedBitmap = new CroppedBitmap(source, sourceRect);
        croppedBitmap.Freeze();
        drawingContext.DrawImage(croppedBitmap, destinationRect);
    }

    private static TimeSpan GetGifFrameDelay(BitmapFrame frame)
    {
        const int defaultDelayMilliseconds = 100;
        const int minimumDelayMilliseconds = 20;

        try
        {
            if (frame.Metadata is BitmapMetadata metadata &&
                metadata.ContainsQuery("/grctlext/Delay") &&
                metadata.GetQuery("/grctlext/Delay") is { } delayValue)
            {
                var hundredthsOfSecond = Convert.ToInt32(delayValue);
                if (hundredthsOfSecond > 0)
                {
                    return TimeSpan.FromMilliseconds(Math.Max(minimumDelayMilliseconds, hundredthsOfSecond * 10));
                }
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FormatException or OverflowException)
        {
        }

        return TimeSpan.FromMilliseconds(defaultDelayMilliseconds);
    }

    private static ImageSourceDetails? ReadImageSourceDetails(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath))
            {
                return null;
            }

            using var stream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 128 * 1024);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            var frame = decoder.Frames[0];
            return new ImageSourceDetails(
                frame.PixelWidth,
                frame.PixelHeight,
                frame.Format.ToString(),
                frame.Format.BitsPerPixel,
                frame.DpiX,
                frame.DpiY,
                decoder.Frames.Count,
                HasColorProfile(frame));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static bool HasColorProfile(BitmapFrame frame)
    {
        try
        {
            return frame.ColorContexts is { Count: > 0 };
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string FormatDpi(double dpi)
    {
        return dpi.ToString("0.##");
    }

    private static string FormatByteSize(long byteSize)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)byteSize;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private void UpdateCurrentImageTitle()
    {
        if (string.IsNullOrWhiteSpace(_currentImagePath))
        {
            Title = "LetMeSee";
            return;
        }

        UpdateWindowTitle(_currentImagePath, _currentImage);
    }

    private void UpdateWindowTitle(string imagePath, BitmapSource? image)
    {
        var fileName = Path.GetFileName(imagePath);
        Title = image is null
            ? $"LetMeSee - {fileName}"
            : $"LetMeSee - {fileName} ({image.PixelWidth} x {image.PixelHeight})";
    }

    private void SetImageDisplaySize(BitmapSource image)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _imageWidth = Math.Max(1, image.PixelWidth / dpi.DpiScaleX);
        _imageHeight = Math.Max(1, image.PixelHeight / dpi.DpiScaleY);
    }

    private void ResizeWindowForImage(bool keepWindowPosition = false)
    {
        if (_currentImage is null)
        {
            ResetMinimumWindowSize();
            return;
        }

        if (_isFullScreen || WindowState == WindowState.Maximized)
        {
            return;
        }

        var workArea = GetCurrentMonitorWorkArea();
        var targetViewport = CalculateTargetViewportSize(workArea, _imageWidth, _imageHeight);
        ResetMinimumWindowSize();
        ResizeWindowForViewportSize(targetViewport, workArea, keepWindowPosition);
        ApplyMinimumWindowSizeForViewport(targetViewport);
    }

    private void ResizeWindowForCurrentScale(bool keepWindowPosition = true)
    {
        if (_currentImage is null || _isFullScreen || WindowState == WindowState.Maximized)
        {
            return;
        }

        var workArea = GetCurrentMonitorWorkArea();
        var scaledWidth = _imageWidth * _zoomScale;
        var scaledHeight = _imageHeight * _zoomScale;
        var targetViewport = CalculateTargetViewportSize(workArea, scaledWidth, scaledHeight);

        ResetMinimumWindowSize();
        ResizeWindowForViewportSize(targetViewport, workArea, keepWindowPosition);
        ApplyMinimumWindowSizeForViewport(targetViewport);
    }

    private void ApplyMinimumWindowSizeForCurrentImage()
    {
        if (_currentImage is null || _isFullScreen || WindowState == WindowState.Maximized)
        {
            return;
        }

        var targetViewport = CalculateTargetViewportSize(GetCurrentMonitorWorkArea(), _imageWidth, _imageHeight);
        ApplyMinimumWindowSizeForViewport(targetViewport);
    }

    private void ApplyMinimumWindowSizeForViewport(Size targetViewport)
    {
        UpdateLayout();

        var widthDecoration = Math.Max(0, ActualWidth - Viewport.ActualWidth);
        var heightDecoration = Math.Max(0, ActualHeight - Viewport.ActualHeight);

        MinWidth = Math.Max(MinimumWindowWidth, targetViewport.Width + widthDecoration);
        MinHeight = Math.Max(MinimumWindowHeight, targetViewport.Height + heightDecoration);
    }

    private void ResetMinimumWindowSize()
    {
        MinWidth = MinimumWindowWidth;
        MinHeight = MinimumWindowHeight;
    }

    private void ResizeWindowForViewportSize(Size targetViewport, Rect workArea, bool keepWindowPosition = false)
    {
        var previousLeft = Left;
        var previousTop = Top;

        UpdateLayout();

        for (var i = 0; i < 4; i++)
        {
            var widthDelta = targetViewport.Width - Viewport.ActualWidth;
            var heightDelta = targetViewport.Height - Viewport.ActualHeight;

            if (Math.Abs(widthDelta) < 0.5 && Math.Abs(heightDelta) < 0.5)
            {
                break;
            }

            Width = Math.Min(workArea.Width, Math.Max(MinWidth, ActualWidth + widthDelta));
            Height = Math.Min(workArea.Height, Math.Max(MinHeight, ActualHeight + heightDelta));
            UpdateLayout();
        }

        if (keepWindowPosition)
        {
            Left = ClampWindowCoordinate(previousLeft, workArea.Left, workArea.Right - Width);
            Top = ClampWindowCoordinate(previousTop, workArea.Top, workArea.Bottom - Height);
        }
        else
        {
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
        }

        UpdateLayout();
    }

    private static double ClampWindowCoordinate(double value, double min, double max)
    {
        return max >= min ? Math.Clamp(value, min, max) : min;
    }

    private Size CalculateTargetViewportSize(Rect workArea, double desiredWidth, double desiredHeight)
    {
        var maxViewportWidth = Math.Max(MinimumViewportWidth, workArea.Width - WindowEdgeMargin);
        var maxViewportHeight = Math.Max(MinimumViewportHeight, workArea.Height - WindowEdgeMargin);
        var scale = Math.Min(1, Math.Min(maxViewportWidth / desiredWidth, maxViewportHeight / desiredHeight));

        return new Size(
            Math.Max(1, desiredWidth * scale),
            Math.Max(1, desiredHeight * scale));
    }

    private void ActualSize()
    {
        SetFixedScale(1);
    }

    private void ZoomAtViewportCenter(double factor)
    {
        Zoom(factor);
    }

    private void ZoomAt(Point viewportPoint, double factor)
    {
        Zoom(factor);
    }

    private void SetScale(double scale)
    {
        _zoomScale = scale;
    }

    private void SetFixedScale(double scale)
    {
        if (_currentImage is null)
        {
            return;
        }

        SetScale(scale);
        _isFitMode = false;
        ResizeWindowForCurrentScale();
        ResetImageOffset();
        ShowScaledImage(scale, stretchUniform: false);
    }

    private void Zoom(double factor)
    {
        if (_currentImage is null)
        {
            return;
        }

        var baseScale = _isFitMode
            ? Math.Min(Viewport.ActualWidth / _imageWidth, Viewport.ActualHeight / _imageHeight)
            : _zoomScale;
        var scale = Math.Clamp(baseScale * factor, 0.02, 64);
        SetScale(scale);
        _isFitMode = false;
        ResizeWindowForCurrentScale();
        ShowScaledImage(scale, stretchUniform: false);
    }

    private void ShowScaledImage(double scale, bool stretchUniform)
    {
        if (_currentImage is null)
        {
            return;
        }

        _zoomScale = scale;
        ImageView.Stretch = Stretch.Fill;
        ImageView.Width = Math.Max(1, _imageWidth * scale);
        ImageView.Height = Math.Max(1, _imageHeight * scale);

        if (stretchUniform)
        {
            ResetImageOffset();
        }
        else
        {
            ClampImageOffset();
        }
    }

    private bool PanImageWithKeyboard(int horizontalDirection, int verticalDirection)
    {
        if (!GetImageOverflow(out var overflowX, out var overflowY))
        {
            return false;
        }

        if (overflowX > 0 && horizontalDirection != 0)
        {
            _imageOffsetX -= horizontalDirection * KeyboardPanStep;
        }

        if (overflowY > 0 && verticalDirection != 0)
        {
            _imageOffsetY -= verticalDirection * KeyboardPanStep;
        }

        ClampImageOffset(overflowX, overflowY);
        return true;
    }

    private bool GetImageOverflow(out double overflowX, out double overflowY)
    {
        overflowX = 0;
        overflowY = 0;

        if (_currentImage is null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return false;
        }

        overflowX = Math.Max(0, ImageView.Width - Viewport.ActualWidth);
        overflowY = Math.Max(0, ImageView.Height - Viewport.ActualHeight);

        return overflowX > 0.5 || overflowY > 0.5;
    }

    private void ClampImageOffset()
    {
        GetImageOverflow(out var overflowX, out var overflowY);
        ClampImageOffset(overflowX, overflowY);
    }

    private void ClampImageOffset(double overflowX, double overflowY)
    {
        _imageOffsetX = overflowX > 0
            ? Math.Clamp(_imageOffsetX, -overflowX / 2, overflowX / 2)
            : 0;
        _imageOffsetY = overflowY > 0
            ? Math.Clamp(_imageOffsetY, -overflowY / 2, overflowY / 2)
            : 0;

        ApplyImageOffset();
    }

    private void ResetImageOffset()
    {
        _imageOffsetX = 0;
        _imageOffsetY = 0;
        ApplyImageOffset();
    }

    private void ApplyImageOffset()
    {
        Canvas.SetLeft(ImageView, (Viewport.ActualWidth - ImageView.Width) / 2 + _imageOffsetX);
        Canvas.SetTop(ImageView, (Viewport.ActualHeight - ImageView.Height) / 2 + _imageOffsetY);
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            MainMenu.Visibility = _previousMenuVisibility;
            Topmost = _previousTopmost;
            ResizeMode = _previousResizeMode;
            SetWindowedTitleBarVisible(true);
            WindowState = WindowState.Normal;
            Left = _previousWindowBounds.Left;
            Top = _previousWindowBounds.Top;
            Width = _previousWindowBounds.Width;
            Height = _previousWindowBounds.Height;
            WindowState = WindowState.Normal;
            _isFullScreen = false;
            _settings.StartFullScreen = false;
            _settings.Save();
            ResizeWindowForImage(keepWindowPosition: true);
        }
        else
        {
            _previousResizeMode = ResizeMode;
            _previousMenuVisibility = MainMenu.Visibility;
            _previousWindowBounds = new Rect(Left, Top, Width, Height);
            _previousTopmost = Topmost;

            MainMenu.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = false;
            _isWindowedTitleBarHidden = false;
            ApplyFullScreenBounds();
            _isFullScreen = true;
            _settings.StartFullScreen = true;
            _settings.Save();
            Activate();
        }

        if (_isFitMode)
        {
            FitToWindow();
        }
    }

    private void ToggleWindowedTitleBarVisibility()
    {
        SetWindowedTitleBarVisible(_isWindowedTitleBarHidden);
        ApplyMinimumWindowSizeForCurrentImage();

        if (Width < MinWidth)
        {
            Width = MinWidth;
        }

        if (Height < MinHeight)
        {
            Height = MinHeight;
        }

        if (_isFitMode)
        {
            FitToWindow();
        }
    }

    private void SetWindowedTitleBarVisible(bool isVisible)
    {
        if (isVisible)
        {
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.SingleBorderWindow;
            _isWindowedTitleBarHidden = false;
            return;
        }

        WindowStyle = WindowStyle.SingleBorderWindow;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(HiddenTitleBarResizeBorderThickness),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        _isWindowedTitleBarHidden = true;
    }

    private void ApplyFullScreenBounds()
    {
        var bounds = GetCurrentMonitorBounds();
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private Rect GetCurrentMonitorBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            return DeviceRectToLogicalRect(monitorInfo.Monitor);
        }

        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            return DeviceRectToLogicalRect(monitorInfo.WorkArea);
        }

        return SystemParameters.WorkArea;
    }

    private Rect DeviceRectToLogicalRect(NativeRect rect)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));

        return new Rect(topLeft, bottomRight);
    }

    private sealed record ImageSourceDetails(
        int PixelWidth,
        int PixelHeight,
        string PixelFormat,
        int BitsPerPixel,
        double DpiX,
        double DpiY,
        int FrameCount,
        bool HasColorProfile);

    private sealed record AnimatedImage(IReadOnlyList<AnimatedImageFrame> Frames);

    private sealed record AnimatedImageFrame(BitmapSource Image, TimeSpan Delay);

    private sealed record GifFrameMetadata(
        int Left,
        int Top,
        int Width,
        int Height,
        int DisposalMethod,
        TimeSpan Delay);

    private sealed class LogicalPathComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            return StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);
    }

    private const int MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
