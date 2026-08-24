using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LetMeSee.Services;

public sealed class ImageLoader
{
    private const long DefaultMaxCacheBytes = 512L * 1024 * 1024;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _leastRecentlyUsedPaths = new();
    private readonly long _maxCacheBytes;
    private long _cacheBytes;

    public ImageLoader()
        : this(DefaultMaxCacheBytes)
    {
    }

    public ImageLoader(long maxCacheBytes)
    {
        _maxCacheBytes = Math.Max(0, maxCacheBytes);
    }

    public async Task<BitmapSource> LoadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        imagePath = Path.GetFullPath(imagePath);

        if (TryGetCachedImage(imagePath, out var cachedImage))
        {
            return cachedImage;
        }

        var decoded = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(imagePath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Image file does not exist.", imagePath);
            }

            // Stamp the cache entry with the file state observed *before* decoding. If the file
            // is rewritten while we read it, the stamp no longer matches and the next lookup
            // re-decodes instead of trusting stale pixels.
            var fileLength = fileInfo.Length;
            var lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;

            using var stream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 128 * 1024);

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            cancellationToken.ThrowIfCancellationRequested();

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();

            return (Image: (BitmapSource)bitmap, FileLength: fileLength, LastWriteTimeUtc: lastWriteTimeUtc);
        }, cancellationToken);

        AddToCache(imagePath, decoded.Image, decoded.FileLength, decoded.LastWriteTimeUtc);
        return decoded.Image;
    }

    public async Task PreloadAsync(IEnumerable<string> imagePaths, CancellationToken cancellationToken = default)
    {
        var paths = imagePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var imagePath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await LoadAsync(imagePath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or COMException)
            {
                // Preloading is opportunistic; a bad neighbor image should not interrupt browsing.
            }
        }
    }

    private bool TryGetCachedImage(string imagePath, out BitmapSource image)
    {
        CacheEntry? entry;
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(imagePath, out entry))
            {
                image = null!;
                return false;
            }
        }

        // Validating the entry touches the disk, so it happens outside the lock; the entry is
        // re-checked afterwards in case another thread replaced it in the meantime.
        var isCurrent = IsCacheEntryCurrent(imagePath, entry);

        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(imagePath, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
            {
                image = null!;
                return false;
            }

            if (!isCurrent)
            {
                RemoveCacheEntry(entry);
                image = null!;
                return false;
            }

            _leastRecentlyUsedPaths.Remove(entry.Node);
            _leastRecentlyUsedPaths.AddFirst(entry.Node);
            image = entry.Image;
            return true;
        }
    }

    private void AddToCache(string imagePath, BitmapSource image, long fileLength, DateTime lastWriteTimeUtc)
    {
        if (_maxCacheBytes == 0)
        {
            return;
        }

        var imageBytes = EstimateBitmapBytes(image);
        if (imageBytes > _maxCacheBytes)
        {
            return;
        }

        var node = new LinkedListNode<string>(imagePath);
        var entry = new CacheEntry(
            image,
            imageBytes,
            fileLength,
            lastWriteTimeUtc,
            node);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(imagePath, out var existingEntry))
            {
                RemoveCacheEntry(existingEntry);
            }

            _leastRecentlyUsedPaths.AddFirst(node);
            _cache[imagePath] = entry;
            _cacheBytes += imageBytes;
            TrimCache();
        }
    }

    private void TrimCache()
    {
        while (_cacheBytes > _maxCacheBytes && _leastRecentlyUsedPaths.Last is { } lastNode)
        {
            RemoveCacheEntry(_cache[lastNode.Value]);
        }
    }

    private void RemoveCacheEntry(CacheEntry entry)
    {
        _leastRecentlyUsedPaths.Remove(entry.Node);
        _cache.Remove(entry.Node.Value);
        _cacheBytes -= entry.ByteSize;
    }

    private static bool IsCacheEntryCurrent(string imagePath, CacheEntry entry)
    {
        try
        {
            var fileInfo = new FileInfo(imagePath);
            return fileInfo.Exists &&
                fileInfo.Length == entry.FileLength &&
                fileInfo.LastWriteTimeUtc == entry.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static long EstimateBitmapBytes(BitmapSource image)
    {
        var bitsPerPixel = image.Format.BitsPerPixel > 0
            ? image.Format.BitsPerPixel
            : PixelFormats.Bgra32.BitsPerPixel;
        return Math.Max(1, ((long)image.PixelWidth * image.PixelHeight * bitsPerPixel + 7) / 8);
    }

    private sealed record CacheEntry(
        BitmapSource Image,
        long ByteSize,
        long FileLength,
        DateTime LastWriteTimeUtc,
        LinkedListNode<string> Node);
}
