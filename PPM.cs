using System;
using System.IO;

namespace Stills2DV
{
	internal class PPM
	{
		public static Image Read(string path)
		{
			throw new NotImplementedException();
		}

		public static void Write(Image output, string path)
		{
			using (var stream = File.Create(path))
			{
				var streamWriter = new StreamWriter(stream);
				streamWriter.Write("P6\n");
				streamWriter.Write("#this ppm was created by stills2dv\n");
				streamWriter.Write("{0} {1}\n", output.Width, output.Height);
				streamWriter.Write("255\n");
				streamWriter.Flush();

				var binaryWriter = new BinaryWriter(stream);
				foreach (var pixel in output.Pixels)
				{
					binaryWriter.Write(pixel.R);
					binaryWriter.Write(pixel.G);
					binaryWriter.Write(pixel.B);
				}
				binaryWriter.Flush();

				stream.Flush();
			}
		}
	}
}