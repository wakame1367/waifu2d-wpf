// A picture's RGBA pixels, as WaifuKit's RGBAImage is: width, height and rgba, not premultiplied.

namespace Waifu2d
{
    sealed class RgbaImage
    {
        public readonly int Width;
        public readonly int Height;
        /// RGBA, not premultiplied: Width by Height by 4.
        public readonly byte[] Rgba;

        public RgbaImage(int width, int height, byte[] rgba)
        {
            Width = width;
            Height = height;
            Rgba = rgba;
        }
    }
}
