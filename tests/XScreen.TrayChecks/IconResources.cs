using System.Runtime.InteropServices;

static class IconResources
{
    public static void Verify(string path, Action<bool, string> check)
    {
        foreach (int size in new[] { 16, 20, 24, 28, 32, 36, 40, 48, 56, 64, 96, 128, 256 })
        {
            var icon = LoadImage(IntPtr.Zero, Path.GetFullPath(path), 1, size, size, 0x10);
            check(icon != IntPtr.Zero, $"Windows LoadImage decodes the {size}px ICO member");
            IconInfo info = default;
            try
            {
                check(GetIconInfo(icon, out info) && info.Color != IntPtr.Zero, $"Decoded {size}px icon has a color bitmap");
                check(GetObject(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap) != 0 && bitmap.Width == size && bitmap.Height == size,
                    $"Native HICON color bitmap is exactly {size}x{size}");
                var header = new BitmapInfo { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 };
                byte[] pixels = new byte[size * size * 4];
                var dc = CreateCompatibleDC(IntPtr.Zero);
                try { check(GetDIBits(dc, info.Color, 0, (uint)size, pixels, ref header, 0) == size, $"Native {size}px bitmap pixels are readable"); }
                finally { DeleteDC(dc); }
                int pink = 0, white = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 2] > 150 && pixels[i + 1] < 120 && pixels[i] < 150 && pixels[i + 3] > 240) pink++;
                    if (pixels[i] > 230 && pixels[i + 1] > 230 && pixels[i + 2] > 230 && pixels[i + 3] > 240) white++;
                }
                check(pink > size * size * .5 && white > size * size * .07,
                    $"Windows decodes recognizable pink/white artwork at {size}px, not corrupt PNG pixels");
                int left = (size / 2 * size) * 4 + 3, top = (size / 2) * 4 + 3;
                check(pixels[left] == 255 && pixels[top] == 255 && pixels[3] < 128,
                    $"Native {size}px image has opaque full-size edges and a transparent rounded corner");
            }
            finally
            {
                if (info.Color != IntPtr.Zero) DeleteObject(info.Color);
                if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
                if (icon != IntPtr.Zero) DestroyIcon(icon);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct IconInfo { public int IsIcon; public uint X, Y; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int X, Y; public uint Used, Important; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr obj, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint count, [Out] byte[] pixels, ref BitmapInfo info, uint usage);
}
