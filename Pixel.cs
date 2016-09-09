using System;
using System.Drawing;

namespace Stills2DV
{
	struct Pixel
	{
		public byte A;
		public byte B;
		public byte G;
		public byte R;

		public static Pixel FromArgb(int value)
		{
			return FromArgb((value >> 24) & 0x0FF, (value >> 16) & 0x0FF, (value >> 8) & 0x0FF, value & 0x0FF);
		}

		public static Pixel FromArgb(int red, int green, int blue)
		{
			return FromArgb(255, red, green, blue);
		}

		private static void Clamp(ref int value)
		{
			value = Math.Max(0, Math.Min(255, value));
		}

		public static Pixel FromArgb(int alpha, int red, int green, int blue)
		{
			Clamp(ref alpha);
			Clamp(ref red);
			Clamp(ref green);
			Clamp(ref blue);

			return new Pixel
			{
				A = (byte)alpha,
				R = (byte)red,
				G = (byte)green,
				B = (byte)blue
			};
		}

		public static Pixel FromColor(Color value)
		{
			return new Pixel
			{
				A = value.A,
				R = value.R,
				G = value.G,
				B = value.B
			};
		}

		public int ToArgb()
		{
			return (int)((uint)A << 24) + (R << 16) + (G << 8) + B;
		}
	}
}
