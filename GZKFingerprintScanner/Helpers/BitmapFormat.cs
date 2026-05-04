using System;
using System.Runtime.InteropServices;
using System.IO;

namespace GZKFingerprintScanner.Helpers
{
    public class BitmapFormat
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BITMAPFILEHEADER
        {
            public ushort bfType;
            public int bfSize;
            public ushort bfReserved1;
            public ushort bfReserved2;
            public int bfOffBits;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        public struct MASK
        {
            public byte bluemask;
            public byte greenmask;
            public byte redmask;
            public byte rgbReserved;
        }

        public static void RotatePic(byte[] BmpBuf, int width, int height, ref byte[] ResBuf)
        {
            int BmpBuflen = width * height;
            for (int RowLoop = 0; RowLoop < BmpBuflen; RowLoop += width)
            {
                for (int ColLoop = 0; ColLoop < width; ColLoop++)
                {
                    ResBuf[RowLoop + ColLoop] = BmpBuf[BmpBuflen - RowLoop - width + ColLoop];
                }
            }
        }

        public static byte[] StructToBytes(object StructObj, int Size)
        {
            int StructSize = Marshal.SizeOf(StructObj);
            byte[] GetBytes = new byte[StructSize];
            IntPtr StructPtr = Marshal.AllocHGlobal(StructSize);
            Marshal.StructureToPtr(StructObj, StructPtr, false);
            Marshal.Copy(StructPtr, GetBytes, 0, StructSize);
            Marshal.FreeHGlobal(StructPtr);

            if (Size == 14 && StructSize == 14)
            {
                return GetBytes;
            }
            if (Size == 14 && StructSize >= 16)
            {
                byte[] NewBytes = new byte[14];
                Array.Copy(GetBytes, 0, NewBytes, 0, 2);
                Array.Copy(GetBytes, 4, NewBytes, 2, 12);
                return NewBytes;
            }
            return GetBytes;
        }

        public static void GetBitmap(byte[] buffer, int nWidth, int nHeight, ref MemoryStream ms)
        {
            ushort m_nBitCount = 8;
            int m_nColorTableEntries = 256;
            byte[] ResBuf = new byte[nWidth * nHeight];

            BITMAPFILEHEADER BmpHeader = new BITMAPFILEHEADER();
            BITMAPINFOHEADER BmpInfoHeader = new BITMAPINFOHEADER();

            int w = (((nWidth + 3) / 4) * 4);

            BmpInfoHeader.biSize = Marshal.SizeOf(BmpInfoHeader);
            BmpInfoHeader.biWidth = nWidth;
            BmpInfoHeader.biHeight = nHeight;
            BmpInfoHeader.biPlanes = 1;
            BmpInfoHeader.biBitCount = m_nBitCount;
            BmpInfoHeader.biClrUsed = m_nColorTableEntries;
            BmpInfoHeader.biClrImportant = m_nColorTableEntries;

            BmpHeader.bfType = 0x4D42;
            BmpHeader.bfOffBits = 14 + Marshal.SizeOf(BmpInfoHeader) + m_nColorTableEntries * 4;
            BmpHeader.bfSize = BmpHeader.bfOffBits + (w * nHeight);

            ms.Write(StructToBytes(BmpHeader, 14), 0, 14);
            ms.Write(StructToBytes(BmpInfoHeader, Marshal.SizeOf(BmpInfoHeader)), 0, Marshal.SizeOf(BmpInfoHeader));

            for (int i = 0; i < m_nColorTableEntries; i++)
            {
                ms.WriteByte((byte)i);
                ms.WriteByte((byte)i);
                ms.WriteByte((byte)i);
                ms.WriteByte(0);
            }

            RotatePic(buffer, nWidth, nHeight, ref ResBuf);

            for (int i = 0; i < nHeight; i++)
            {
                ms.Write(ResBuf, i * nWidth, nWidth);
                int padding = w - nWidth;
                if (padding > 0) ms.Write(new byte[padding], 0, padding);
            }
        }
    }
}
