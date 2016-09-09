using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Stills2DV
{
	class Program
	{
		private const int BytesPerPixel = 4;

		private const int Center = -2000000001;
		private const int Left = -2000000002;
		private const int Right = -2000000003;
		private const int Top = -2000000004;
		private const int Bottom = -2000000005;
		private const int FitWidth = -2000000006;
		private const int FitHeight = -2000000007;
		private const int Fit = -2000000008;
		private const int TopRight = -2000000009;
		private const int TopLeft = -2000000010;
		private const int BottomRight = -2000000011;
		private const int BottomLeft = -2000000012;

		private const int PPM_FORMAT = 0;
		private const int JPG_FORMAT = 1;

		private static int _outputWidth = 720;
		private static int _outputHeight = 480;
		private static bool _showOutput = false;
		private static bool _fastRender = false;
		private static bool _motionBlur = true;
		private static int _jpegQuality = 90;

		// just an improbable value so we can determinate that there is no value to read.
		private const int LOST_IN_THE_WOOD = -2147483646;

		private static Image _cachedImage;
		private static Image _im;
		private static int _serialIndex;
		private static Point[] _prev;
		static int _lastSerial = -1;
		static float _lastFx, _lastFy, _lastFzoom;

		// Movement.cs

		// defaults
		private static float _framerate = 30.00f;
		private static int _outputFormat = PPM_FORMAT;
		private static Pixel _defaultColor = Pixel.FromColor(Color.Black);

		private static double _panSmoothRatio = 0.2;
		private static double _zoomSmoothRatio = 0.2;


		private static int _sharpness = 1;

		private static int _frameCount = 0;
		private static string _tmpdir;
		private const string Usage = "stills2dv [-tmpdir <temp dir>] [-showoutput] [-fastrender ^ -nomotionblur] <scriptfile>\n";
		private static string _lastFilename;
		private static int _lastsx = -1, _lastsy, _lastx, _lasty;

		//private static char** split(char* );
		private static Movement _lastp, _ms;

		//private FILE* scriptfile = NULL;
		//private FILE* output = NULL;


		//private char biggestblowfilename [2048];
		//private int biggestzoom, biggestx, biggesty;
		//private float biggestarea = 0.0;

		private int stricmp(string a, string b)
		{
			return string.CompareOrdinal(a, b);
		}

		private static void clear_prev_buffer()
		{
			for (var i = 0; i < _prev.Length; i++)
			{
				_prev[i] = new Point(LOST_IN_THE_WOOD, LOST_IN_THE_WOOD);
			}
		}

		private static int check_prev_buffer(int width, int height)
		{
			var size = width * height; 
			if (_prev == null || _prev.Length < size)
			{
				_prev = new Point[size];
				clear_prev_buffer();
			}
			return 0;
		}

		 private static Image OpenImage(string path, bool cache)
		{
			if (_cachedImage.Path != null && _cachedImage.Path.Equals(path))
				return _cachedImage;

			var len = path.Length;
			if (len < 5)
				throw new ArgumentException(@"path is too short.", @"path");

			var ext = Path.GetExtension(path);

			Image image;
			if (ext != null && ext.EndsWith(@"ppm", StringComparison.OrdinalIgnoreCase))
			{
				image = PPM.Read(path);
			}
			else
			{
				image = Readimage(path);
			}
			image.Serial = _serialIndex++;
	
			if (cache)
				_cachedImage = image;

			return image;
		}

		private static int power(int value, int n)
		{
			if (value == 0)
				return 1;

			var p = 1;
			for (var i = 0; i < n; i++)
			{
				p *= value;
			}
			return p;
		}

		private static Image Readimage(string path)
		{
			const PixelFormat pixelFormat = PixelFormat.Format32bppArgb;

			// Load file from disk
			var original = (Bitmap) System.Drawing.Image.FromFile(path);
			var rectangle = new Rectangle(0, 0, original.Width, original.Height);

			// Optionally convert pixel format
			Bitmap clone;
			if (original.PixelFormat == pixelFormat)
			{
				clone = original;
			}
			else
			{
				clone = new Bitmap(original.Width, original.Height, pixelFormat);
				using (var graphics = Graphics.FromImage(clone))
					graphics.DrawImage(original, rectangle);
			}

			// Convert image to byte[]
			var pixels = new byte[clone.Width * clone.Height * BytesPerPixel];
			var bitmapData = clone.LockBits(rectangle, ImageLockMode.ReadOnly, clone.PixelFormat);
			var ptr = bitmapData.Scan0;
			Marshal.Copy(ptr, pixels, 0, pixels.Length);
			clone.UnlockBits(bitmapData);

			var outputImage = new Image
			{
				Pixels = new Pixel[bitmapData.Width * bitmapData.Height]
			};
			for (var i = 0; i < outputImage.Pixels.Length; i++)
			{
				var colorValue = BitConverter.ToInt32(pixels, i * BytesPerPixel);
				outputImage.Pixels[i] = Pixel.FromArgb(colorValue);
			}

			outputImage.Path = path;
			outputImage.Height = bitmapData.Height;
			outputImage.Width = bitmapData.Width;

			return outputImage;
		}

		private static Pixel[] ResizeFast(Image im, float fx, float fy, float fzoom)
		{
			Pixel[] res;
			float fw, frx, fry, frw, frh, fdx, fdy;
			int bufsize, outoffset, inoffset, ix, iy, i, j;

			bufsize = _outputWidth * _outputHeight;
			res = new Pixel[bufsize];

			fw = im.Width;
			frw = _outputWidth;
			frh = _outputHeight;

			// printf("About to big loop with im=%p, im->data=%p, res=%p, outputwidth=%d, outputheight=%d\n",im, im->data, res, outputwidth, outputheight);
			for(iy=0; iy < _outputHeight; iy++)
			{
				fry = iy;
				for (ix = 0; ix < _outputWidth; ix++)
				{
					outoffset = (iy * _outputWidth + ix);
					frx = ix;
					fdx = fx - (fw / (fzoom * 2f)) + (frx * fw / (frw * fzoom));
					fdy = fy - (fw * frh / (fzoom * 2f * frw)) + (fry * fw / (frw * fzoom));
					j = (int) fdx;
					i = (int) fdy;

					if ((i >= 0) && (j >= 0) && (j < im.Width) && (i < im.Height))
					{
						inoffset = (i * im.Width + j);
						res[outoffset] = im.Pixels[inoffset];
					}
					else
					{
						res[outoffset] = _defaultColor;
					}
				}
			}
			// printf("After the big loop\n");
			return res;
		}

		private static Pixel[] ResizeNoBlur(Image im, float fx, float fy, float fzoom)
		{
			Pixel[] res;

			float rpw, fw, frx, fry, frw, frh, fdx, fdy, fstartx, fstarty, fendx, fendy, fred, icolor; 
			float fweight, ftotalweight, fdelta, fgreen, fblue, fi, fj, fcolor;
			int bufsize, outoffset, inoffset, ix, iy, i, j, istartx, istarty, iendx, iendy, pprev;

			bufsize = _outputWidth * _outputHeight;
			res = new Pixel[bufsize];
			for(i = 0; i < bufsize ; i++)
			{
				res[i] = _defaultColor;
			}

			fw = im.Width;
			frw = _outputWidth;
			frh = _outputHeight;

			rpw=fw/(frw * fzoom * _sharpness);
			if(rpw<1.33333)
				rpw=1.33333f;

			// printf("About to big loop with im=%p, im->data=%p, res=%p, outputwidth=%d, outputheight=%d\n",im, im->data, res, outputwidth, outputheight);
			check_prev_buffer(_outputWidth, _outputHeight);
			pprev = 0;

			for(iy = 0; iy < _outputHeight; iy++)
			{
				fry = iy;
				for (ix = 0; ix < _outputWidth; ix++)
				{
					outoffset = (iy * _outputWidth + ix);
					frx = ix;
					fdx = fx - (fw / (fzoom * 2.0f)) + (frx * fw / (frw * fzoom));
					fdy = fy - (fw * frh / (fzoom * 2.0f * frw)) + (fry * fw / (frw * fzoom));

					if (_motionBlur)
					{
						_prev[pprev] = new Point((int) fdx, (int) fdy);
						pprev++;
					}

					fstartx = fdx - rpw;
					fendx = fdx + rpw;
					fstarty = fdy - rpw;
					fendy = fdy + rpw;
					istartx = (int) fstartx;
					istarty = (int) fstarty;
					iendx = (int) fendx;
					iendy = (int) fendy;
					fred = 0f;
					fgreen = 0f;
					fblue = 0f;
					ftotalweight = 0f;

					for (i = istarty; i <= iendy; i++)
					{
						for (j = istartx; j <= iendx; j++)
						{
							if ((i >= 0) && (j >= 0) && (j < im.Width) && (i < im.Height))
							{
								inoffset = (i * im.Width + j);
								fi = i;
								fj = j;
								fdelta = (float) Math.Sqrt(((fj - fdx) * (fj - fdx)) + ((fi - fdy) * (fi - fdy)));
								if ((rpw - fdelta) <= 0.0)
								{
								}
								else
								{
									if ((rpw - fdelta) > 0.001)
										fweight = rpw - fdelta;
									else
										fweight = 0.001f;

									ftotalweight = ftotalweight + fweight;

									fcolor = im.Pixels[inoffset].R;
									fred = fred + (fcolor * fweight);

									fcolor = im.Pixels[inoffset].G;
									fgreen = fgreen + (fcolor * fweight);

									fcolor = im.Pixels[inoffset].B;
									fblue = fblue + (fcolor * fweight);
								}
							}
						}
					}

					if (ftotalweight <= 0.0)
					{
						// printf("totalweight under 0???\n");
					}
					else
					{
						fred = fred / ftotalweight;
						fgreen = fgreen / ftotalweight;
						fblue = fblue / ftotalweight;

						icolor = fred;
						res[outoffset].R = (byte)icolor;
						
						icolor = fgreen;
						res[outoffset].G = (byte)icolor;
						
						icolor = fblue;
						res[outoffset].B = (byte)icolor;
					}
				}
			}
			// printf("After the big loop\n");
			return res;
		}

		private static void MotionBlur(ref Image im, ref float fred, ref float fgreen, ref float fblue, ref float ftotalweight, int x1, int y1, int x2, int y2)
		{
			int inoffset, midx, midy;
			float r, g, b;

			if (x2 == LOST_IN_THE_WOOD)
				return;

			x1 = x1;
			y1 = y1;
			x2 = x2;
			y2 = y2;
			midx = (x1 + x2) / 2;
			midy = (y1 + y2) / 2;
			if ((midx == x1) && (midy == y1))
				return;

			if ((midx == x2) && (midy == y2))
				return;

			MotionBlur(ref im, ref fred, ref fgreen, ref fblue, ref ftotalweight, x1, y1, midx, midy);
			MotionBlur(ref im, ref fred, ref fgreen, ref fblue, ref ftotalweight, x2, y2, midx, midy);

			if ((midx >= 0) && (midy >= 0) && (midx < im.Width) && (midy < im.Height))
			{
				inoffset = (midy * im.Width + midx);
				r = im.Pixels[inoffset].R;
				g = im.Pixels[inoffset].G;
				b = im.Pixels[inoffset].B;
			}
			else
			{
				r = _defaultColor.R;
				g = _defaultColor.G;
				b = _defaultColor.B;
			}

			fred = fred + r;
			fgreen = fgreen + g;
			fblue = fblue + b;
			ftotalweight = ftotalweight + 1.0f;
		}

		static Pixel[] Resize(Image im, float fx, float fy, float fzoom)
		{
			Pixel[] res;
			float rpw, fw, frx, fry, frw, frh, fdx, fdy, fstartx, fstarty, fendx, fendy, fred, icolor;
			float fweight, ftotalweight, fdelta, fgreen, fblue, fi, fj, fcolor;
			int bufsize, outoffset, inoffset, ix, iy, i, j, istartx, istarty, iendx, iendy, pprev;

			if (_lastSerial != im.Serial)
			{
				_lastFx = fx;
				_lastFy = fy;
				_lastFzoom = fzoom;

				_lastSerial = im.Serial;
				return ResizeNoBlur(im, fx, fy, fzoom);
			}

			if ((_lastFx == fx) && (_lastFy == fy) && (_lastFzoom == fzoom))
			{
				return ResizeNoBlur(im, fx, fy, fzoom);
			}

			bufsize = _outputWidth * _outputHeight;
			res = new Pixel[bufsize];
			for (i = 0; i < bufsize; i++)
			{
				res[i] = _defaultColor;
			}

			fw = im.Width;
			frw = _outputWidth;
			frh = _outputHeight;

			rpw = fw / (frw * fzoom * _sharpness);
			if (rpw < 1.33333)
				rpw = 1.33333f;

			// printf("About to big loop with im=%p, im->data=%p, res=%p, outputwidth=%d, outputheight=%d\n",im, im->data, res, outputwidth, outputheight);
			check_prev_buffer(_outputWidth, _outputHeight);
			pprev = 0;

			for (iy = 0; iy < _outputHeight; iy++)
			{
				fry = iy;
				for (ix = 0; ix < _outputWidth; ix++)
				{
					outoffset = (iy * _outputWidth + ix);
					frx = ix;
					fdx = fx - (fw / (fzoom * 2.0f)) + (frx * fw / (frw * fzoom));
					fdy = fy - (fw * frh / (fzoom * 2.0f * frw)) + (fry * fw / (frw * fzoom));
					fstartx = fdx - rpw;
					fendx = fdx + rpw;
					fstarty = fdy - rpw;
					fendy = fdy + rpw;
					istartx = (int) fstartx;
					istarty = (int) fstarty;
					iendx = (int) fendx;
					iendy = (int) fendy;
					fred = 0f;
					fgreen = 0f;
					fblue = 0f;
					ftotalweight = 0f;

					for (i = istarty; i <= iendy; i++)
					{
						for (j = istartx; j <= iendx; j++)
						{
							if ((i >= 0) && (j >= 0) && (j < im.Width) && (i < im.Height))
							{
								inoffset = (i * im.Width + j);
								fi = i;
								fj = j;
								fdelta = (float) Math.Sqrt(((fj - fdx) * (fj - fdx)) + ((fi - fdy) * (fi - fdy)));

								if ((rpw - fdelta) <= 0.0)
								{
								}
								else
								{
									if ((rpw - fdelta) > 0.001)
										fweight = rpw - fdelta;
									else
										fweight = 0.001f;

									ftotalweight = ftotalweight + fweight;

									fcolor = im.Pixels[inoffset].R;
									fred = fred + (fcolor * fweight);

									fcolor = im.Pixels[inoffset].G;
									fgreen = fgreen + (fcolor * fweight);

									fcolor = im.Pixels[inoffset].B;
									fblue = fblue + (fcolor * fweight);
								}
							}
						}
					}

					if (ftotalweight <= 0.0)
					{
						// printf("totalweight under 0???\n");
					}
					else
					{
						fred = fred / ftotalweight;
						fgreen = fgreen / ftotalweight;
						fblue = fblue / ftotalweight;

						if (_motionBlur)
						{
							ftotalweight = 1.0f;
							istartx = (int) fdx;
							istarty = (int) fdy;
							MotionBlur(ref im, ref fred, ref fgreen, ref fblue, ref ftotalweight, istartx, istarty, _prev[pprev].X, _prev[pprev].Y);
							fred = fred / ftotalweight;
							fgreen = fgreen / ftotalweight;
							fblue = fblue / ftotalweight;
							_prev[pprev] = new Point(istartx, istarty);
						}

						icolor = fred;
						res[outoffset].R = (byte) icolor;

						icolor = fgreen;
						res[outoffset].G = (byte) icolor;

						icolor = fblue;
						res[outoffset].B = (byte) icolor;
					}

					if (_motionBlur)
					{
						pprev++;
					}
				}
			}
			// printf("After the big loop\n");
			return res;
		}

		private static void Frame(ref Movement p, float x, float y, float zoom)
		{
			int bytecount, i, ix, iy, jx, jy, lx, ly, resizeX, resizeY, cropX, cropY;

			string fn;
			Pixel[] ppm, crossdata = new Pixel[0];

			Image crossedjpg;
			Image res;

			float ratioA, ratioB;
			Pixel valueA, valueB, mixedvalue;
			float fx, fy;
			float px, py, qx, qy; // precropx, precropy


			// printf("frame(%p, %9.3f, %9.3f, %9.3f);\n", p, x, y, zoom);
			if (_lastsx == -1)
			{
				_lastFilename = string.Empty;
			}

			// Calculate Resize
			fx = p.Width;
			fx = fx * zoom;
			resizeX = (int) fx;
			if(resizeX < _outputWidth)
				resizeX = _outputWidth;

			fy = p.Height;
			fy = fy * zoom;
			resizeY = (int) fy;
			if(resizeY < _outputHeight)
				resizeY=_outputHeight;

			// Calculate Crop
			fx = x * zoom;
			cropX = (int) fx;
			cropX -= _outputWidth / 2;
			if (cropX < 0)
				cropX = 0;

			fy = y * zoom;
			cropY = (int) fy;
			cropY -= _outputHeight / 2;
			if (cropY < 0)
				cropY = 0;

			fn = string.Format("{0}{1}{2:D5}.", _tmpdir, Path.DirectorySeparatorChar, _frameCount);
			_im = OpenImage(p.Filename, true);

			Console.WriteLine("Creating frame #{0} at x={1:0.00} y={2:0.00} and zoom {3:0.00}", _frameCount, x, y, zoom);

			if (_fastRender)
			{
				ppm = ResizeFast(_im, x, y, zoom);
			}
			else
			{
				ppm = Resize(_im, x, y, zoom);
			}

			if (_outputFormat == JPG_FORMAT)
			{
				fn = string.Concat(fn, "jpg");
			}
			else
			{
				fn = string.Concat(fn, "ppm");
			}

			if (p.Crossfade > 0)
			{
				crossedjpg = OpenImage(fn, false);
				crossdata = crossedjpg.Pixels;
			}

			if (crossdata.Length > 0)
			{
				bytecount = _outputWidth * _outputHeight;
				ratioA = p.Crossfade;
				ratioB = p.CrossFrames;

				ratioA = ratioA / ratioB;
				ratioB = 1 - ratioA;
					
				for (i = 0; i < bytecount; i++)
				{
					valueA = crossdata[i];
					valueB = ppm[i];
					mixedvalue = ppm[i];
					mixedvalue.R = (byte) ((valueA.R * ratioA) + (valueB.R * ratioB));
					mixedvalue.G = (byte) ((valueA.G * ratioA) + (valueB.G * ratioB));
					mixedvalue.B = (byte) ((valueA.B * ratioA) + (valueB.B * ratioB));
					ppm[i] = mixedvalue;
				}
				crossdata = new Pixel[0];
				p.Crossfade--;
			}
			else
			{
				p.Crossfade = 0;
				p.CrossFrames = 0;
			}

			res = new Image();
			res.Width = _outputWidth;
			res.Height = _outputHeight;
			res.Path = fn;
			res.Pixels = ppm;

			if (_showOutput)
			{
				ShowImage(res);
			}

			if (_outputFormat == JPG_FORMAT)
			{
				WriteJPG(res, fn);
			}
			else
			{
				PPM.Write(res, fn);
			}

			ppm = null;

			if (p.Filename.Equals(_lastFilename, StringComparison.Ordinal) && _lastsx == resizeX && _lastsy == resizeY && _lastx == cropX && _lasty == cropY)
			{
			}
			else if (resizeX < 740 && resizeY < 490)
			{
				_lastFilename = p.Filename;
				_lastsx = resizeX;
				_lastsy = resizeY;
				_lastx = cropX;
				_lasty = cropY;
			}
			else
			{
				_lastFilename = p.Filename;
				_lastsx = resizeX;
				_lastsy = resizeY;
				_lastx = cropX;
				_lasty = cropY;
				px = cropX;
				px = px / zoom;
				py = cropY;
				py = py / zoom;
				qx = (float) (_outputWidth + (zoom * 2.0) + 1) / zoom;
				qy = (float) (_outputHeight + (zoom * 2.0) + 1) / zoom;
				ix = (int) px;
				iy = (int) py;
				jx = (int) qx;
				jy = (int) qy;
				if ((ix + jx) > p.Width)
					jx = p.Width - ix;

				if ((iy + jy) > p.Height)
					jy = p.Height - iy;

				px = zoom * (float) jx;
				py = zoom * (float) jy;
				qx = ix;
				qx = qx * zoom;
				qy = iy;
				qy = qy * zoom;
				lx = (int) qx;
				ly = (int) qy;
				cropX -= lx;
				cropY -= ly;
			}
			_frameCount++;
		}

		private static void Write(Image output, string fn, ImageCodecInfo encoder = null, EncoderParameters encoderParameters = null)
		{
			var bitmap = new Bitmap(output.Width, output.Height, PixelFormat.Format32bppArgb);

			var pixels = new byte[output.Width * output.Height * BytesPerPixel];
			using (var pixelStream = new MemoryStream(pixels, true))
			using (var writer = new BinaryWriter(pixelStream))
			{
				foreach (var color in output.Pixels)
					writer.Write(color.ToArgb());

				writer.Flush();

				var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
				var ptr = bitmapData.Scan0;
				Marshal.Copy(pixels, 0, ptr, pixels.Length);
				bitmap.UnlockBits(bitmapData);
			}

			var dir = Path.GetDirectoryName(fn);
			if (dir != null && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			if (encoder == null)
				bitmap.Save(fn);
			else
				bitmap.Save(fn, encoder, encoderParameters);
		}

		private static void WriteJPG(Image output, string fn)
		{
			ImageCodecInfo jpgEncoder = null;
			foreach (var encoder in ImageCodecInfo.GetImageEncoders())
			{
				if (encoder.MimeType == @"image/jpeg")
					jpgEncoder = encoder;
			}
			if (jpgEncoder == null)
				throw new Exception(@"Encoder not found: image/jpeg");

			var encoderParameters = new EncoderParameters
			{
				Param = new[]
				{
					new EncoderParameter(Encoder.Quality, _jpegQuality)
				}
			};
			Write(output, fn, jpgEncoder, encoderParameters);
		}

		static void Action(ref Movement p)
		{
			double total;
			double x, y, zoom, f_startx, f_starty, f_endx, f_endy;
			int i, icount;
			total = p.Duration * _framerate;
			icount = (int) total;
			//  printf("action: from (%d, %d) to (%d, %d) in %d frames.\n", p->startx, p->starty, p->endx, p->endy,  icount);
			if (icount < 1)
			{
				icount = 1;
				total = icount;
			}
			f_startx = p.StartX;
			f_starty = p.StartY;
			f_endx = p.EndX;
			f_endy = p.EndY;
			x = f_startx;
			y = f_starty;
			zoom = p.ZoomStart;
			for (i = 0; i < icount; i++)
			{
				x = x + Smoothing.SmoothedStep(f_startx, f_endx, i, (double)icount, _panSmoothRatio);
				y = y + Smoothing.SmoothedStep(f_starty, f_endy, i, (double)icount, _panSmoothRatio);
				zoom = zoom + Smoothing.SmoothedStep((double)p.ZoomStart, (double)p.ZoomEnd, i, (double)icount, _zoomSmoothRatio);
				Frame(ref p, (float)x, (float)y, (float)zoom);
			}
		}

		private static bool Splitted2Struct(ref Movement p, string[] strs)
		{
			if (strs.Length == 0)
				return false;

			p.EndSmooth = 0f;
			p.EndX = Center;
			p.EndY = Center;
			p.Filename = null;
			p.Height = 0;
			p.StartSmooth = 0f;
			p.StartX = Center;
			p.StartY = Center;
			p.ZeCode = 0;
			p.ZsCode = 0;
			p.ZoomStart = 1f;
			p.ZoomEnd = 1f;
			p.Width = 0;

			var actiondata = false;
			var continued = false;

			for (var i = 0; i < strs.Length; i++)
			{
				var str = strs[i];

				string[] location;
				int h;
				int w;
				int x;
				int y;
				float f;
				switch (str.ToLowerInvariant())
				{
					case @"geometry":
					case @"resize":
					case @"resolution":
					case @"size":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						var dimensions = Regex.Split(str, @"[:x]");
						if (dimensions.Length < 2)
							continue;

						if (!int.TryParse(dimensions[0], out w) || !int.TryParse(dimensions[1], out h))
							continue;

						if ((w < 1) || (h < 1))
							continue;

						_outputWidth = w;
						_outputHeight = h;
						break;

					case @"owidth":
					case @"outputwidth":
					case @"width":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!int.TryParse(str, out w) || w <= 0)
							continue;

						_outputWidth = w;
						break;

					case @"height":
					case @"oheight":
					case @"outputheight":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!int.TryParse(str, out h) || h <= 0)
							continue;

						_outputHeight = h;
						break;

					case @"sharpness":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						int s;
						if (!int.TryParse(str, out s) || s <= 0)
							continue;

						_sharpness = s;
						break;

					case @"pansmoothness":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f))
							continue;

						if (f < 0.0)
							f = 0f;

						if (f > 0.5)
							f = 0.5f;

						_panSmoothRatio = f;
						break;

					case @"zoomsmoothness":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f))
							continue;

						if (f < 0.0)
							f = 0f;

						if (f > 0.5)
							f = 0.5f;

						_zoomSmoothRatio = f;
						break;

					case @"jpegquality":
					case @"jpgquality":
					case @"quality":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!int.TryParse(str, out h) || h <= 0)
							continue;

						_jpegQuality = h;
						break;

					case @"fps":
					case @"framerate":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						_framerate = f;
						break;

					case @"output":
					case @"outputtype":
					case @"type":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (str.Equals(@"jpeg", StringComparison.OrdinalIgnoreCase) || str.Equals(@"jpg", StringComparison.OrdinalIgnoreCase))
							_outputFormat = JPG_FORMAT;
						else if (str.Equals(@"ppm", StringComparison.OrdinalIgnoreCase))
							_outputFormat = PPM_FORMAT;

						break;

					case @"backgroundcolor":
					case @"bgcolor":
					case @"defaultcolor":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i].Replace(@"#", string.Empty);

						int c;
						if (!int.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture.NumberFormat, out c))
							continue;

						_defaultColor = Pixel.FromArgb(c);
						break;

					case @"img":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						p.Filename = str;

						_im = OpenImage(str, true);

						p.Height = _im.Height;
						p.Width = _im.Width;
						actiondata = true;
						break;

					case @"crossfade":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						p.Duration = f;
						p.Crossfade = (int)(f * _framerate) - 2;

						if (_frameCount < p.Crossfade)
							p.Crossfade = _frameCount;

						_frameCount -= p.Crossfade;
						p.CrossFrames = p.Crossfade;
						break;

					case @"startpoint":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						location = str.Split(',');
						if (!int.TryParse(location[0], out x) || x < 0)
						{
							p.EndX = p.EndY = p.StartX = p.StartY = Translate(location[0]);
							continue;
						}

						if (!int.TryParse(location[1], out y) || y < 0)
							continue;

						p.StartX = x;
						p.EndX = x;

						p.StartY = y;
						p.EndY = y;

						actiondata = true;
						break;

					case @"endpoint":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						location = str.Split(',');
						if (!int.TryParse(location[0], out x) || x < 0)
						{
							p.EndX = p.EndY = Translate(location[0]);
							continue;
						}

						if (!int.TryParse(location[1], out y) || y < 0)
							continue;

						p.EndX = x;
						p.EndY = y;

						actiondata = true;
						break;

					case @"zoom":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						var zoom = str.Split(',');
						if (zoom.Length == 2)
						{
							if (float.TryParse(zoom[0], out f))
							{
								p.ZoomStart = f;
							}
							else
							{
								p.ZoomStart = 0;
								p.ZsCode = Translate(zoom[0]);
							}

							if (float.TryParse(zoom[1], out f))
							{
								p.ZoomEnd = f;
							}
							else
							{
								p.ZoomEnd = 0;
								p.ZeCode = Translate(zoom[1]);
							}
						}
						else
						{
							if (continued)
							{
								if (float.TryParse(zoom[0], out f))
								{
									p.ZoomEnd = f;
								}
								else
								{
									p.ZoomEnd = 0;
									p.ZeCode = Translate(zoom[0]);
								}
							}
							else
							{
								if (float.TryParse(zoom[0], out f))
								{
									p.ZoomStart = f;
									p.ZoomEnd = f;
								}
								else
								{
									p.ZoomStart = 0;
									p.ZoomEnd = 0;
									var code = Translate(zoom[0]);
									p.ZeCode = code;
									p.ZsCode = code;
								}
							}
						}
						actiondata = true;
						break;

					case @"duration":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						p.Duration = f;
						actiondata = true;
						break;

					case @"startsmooth":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						p.StartSmooth = f;
						actiondata = true;
						break;

					case @"endsmooth":
						if (i == strs.Length)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						p.EndSmooth = f;
						actiondata = true;
						break;

					case @"continue":
						continued = true;
						p.EndX = p.StartX = _lastp.EndX;
						p.EndY = p.StartY = _lastp.EndY;
						p.Filename = _lastp.Filename;
						p.Height = _lastp.Height;
						p.Width = _lastp.Width;
						p.ZoomEnd = p.ZoomStart = _lastp.ZoomEnd;
						actiondata = true;
						break;

					default:
						Console.WriteLine("Unknown command: {0}", str);
						break;
				}
			}

			if (actiondata)
			{
				Normalize(ref p);
				_lastp = p;
				return true;
			}
			return false;
		}

		private static void Normalize(ref Movement p)
		{
			float oresx = _outputWidth;
			float oresy = _outputHeight;
			float iresx = p.Width;
			float iresy = p.Height;
			var fitheightzoom = oresy * iresx / (oresx * iresy);

			var fitzoom = fitheightzoom < 1.0
				? fitheightzoom
				: 1f;

			if (p.ZeCode == FitHeight)
				p.ZoomEnd = fitheightzoom;

			if (p.ZeCode == FitWidth)
				p.ZoomEnd = 1f;

			if (p.ZeCode == Fit)
				p.ZoomEnd = fitzoom;

			if (p.ZsCode == FitHeight)
				p.ZoomStart = fitheightzoom;

			if (p.ZsCode == FitWidth)
				p.ZoomStart = 1f;

			if (p.ZsCode == Fit)
				p.ZoomStart = fitzoom;

			switch (p.StartX)
			{
				case Center:
					p.StartX = p.Width / 2;
					p.StartY = p.Height / 2;
					break;

				case Left:
					p.StartX = (int)(iresx / (p.ZoomStart * 2));
					p.StartY = p.Height / 2;
					break;

				case Right:
					p.StartX = (int)(iresx - iresx / (p.ZoomStart * 2));
					p.StartY = p.Height / 2;
					break;

				case Bottom:
					p.StartX = p.Width / 2;
					p.StartY = (int)(iresy - iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case Top:
					p.StartX = p.Width / 2;
					p.StartY = (int)(iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case TopRight:
					p.StartX = (int)(iresx - iresx / (p.ZoomStart * 2));
					p.StartY = (int)(iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case TopLeft:
					p.StartX = (int)(iresx / (p.ZoomStart * 2));
					p.StartY = (int)(iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case BottomRight:
					p.StartX = (int)(iresx - iresx / (p.ZoomStart * 2));
					p.StartY = (int)(iresy - iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case BottomLeft:
					p.StartX = (int)(iresx / (p.ZoomStart * 2));
					p.StartY = (int)(iresy - iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;
			}

			switch (p.EndX)
			{
				case Center:
					p.EndX = p.Width / 2;
					p.EndY = p.Height / 2;
					break;

				case Left:
					p.EndX = (int)(iresx / (p.ZoomEnd * 2));
					p.EndY = p.Height / 2;
					break;

				case Right:
					p.EndX = (int)(iresx - iresx / (p.ZoomEnd * 2));
					p.EndY = p.Height / 2;
					break;

				case Bottom:
					p.EndX = p.Width / 2;
					p.EndY = (int)(iresy - iresx * oresy / (p.ZoomEnd * 2 * oresx));
					break;

				case Top:
					p.EndX = p.Width / 2;
					p.EndY = (int)(iresx * oresy / (p.ZoomEnd * 2 * oresx));
					break;

				case TopRight:
					p.EndX = (int)(iresx - iresx / (p.ZoomStart * 2));
					p.EndY = (int)(iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case TopLeft:
					p.EndX = (int)(iresx / (p.ZoomStart * 2));
					p.EndY = (int)(iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case BottomRight:
					p.EndX = (int)(iresx - iresx / (p.ZoomStart * 2));
					p.EndY = (int)(iresy - iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;

				case BottomLeft:
					p.EndX = (int)(iresx / (p.ZoomStart * 2));
					p.EndY = (int)(iresy - iresx * oresy / (p.ZoomStart * 2 * oresx));
					break;
			}
		}


		private static string[] Split(string ligne)
		{
			return Regex.Split(ligne, @"\s");
		}

		private static int Translate(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
				throw new ArgumentNullException(@"s");

			switch (s.ToLowerInvariant())
			{
				case @"center":
					return Center;

				case @"left":
					return Left;

				case @"right":
					return Right;

				case @"top":
					return Top;

				case @"bottom":
					return Bottom;

				case @"fitwidth":
					return FitWidth;

				case @"fitheight":
					return FitHeight;

				case @"fit":
					return Fit;

				case @"topright":
				case @"righttop":
					return TopRight;

				case @"lefttop":
				case @"topleft":
					return TopLeft;

				case @"bottomright":
				case @"rightbottom":
					return BottomRight;

				case @"leftbottom":
				case @"bottomleft":
					return BottomLeft;

				default:
					return 0;
			}
		}

		private static void ShowImage(Image image)
		{
			throw new NotImplementedException();
		}

		private static void Main(string[] args)
		{
			_tmpdir = Environment.GetEnvironmentVariable(@"TMPDIR");
			if (string.IsNullOrEmpty(_tmpdir))
				_tmpdir = Path.GetTempPath();

			string fn = null;

			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];

				if (arg.StartsWith(@"-"))
				{
					switch (arg.ToLowerInvariant())
					{
						case "-fastrender":
							Console.WriteLine("Setting fastrender to true!");
							_fastRender = true;
							break;

						case "-showoutput":
							Console.WriteLine("Setting showouput to true!");
							_showOutput = true;
							break;

						case "-tmpdir":
							_tmpdir = args[i + 1];
							i++;
							break;

						default:
							Console.WriteLine("Unknown command: {0}", arg);
							Console.WriteLine("Usage: {0}", Usage);
							Environment.Exit(Errno.EPERM);
							return;
					}
				}
				else
				{
					if (fn != null)
					{
						Console.WriteLine("Usage: {0}", Usage);
						Environment.Exit(Errno.EPERM);
					}

					fn = arg;
				}
			}

			if (string.IsNullOrWhiteSpace(fn))
			{
				Console.WriteLine("Usage: {0}", Usage);
				Environment.Exit(Errno.EPERM);
			}

			try
			{
				var scriptLines = File.ReadAllLines(fn);

				foreach (var scriptLine in scriptLines)
				{
					Console.WriteLine(scriptLine);

					var splitted = Split(scriptLine);
					if (splitted == null)
						continue;

					if (Splitted2Struct(ref _ms, splitted))
						Action(ref _ms);
				}
			}
			catch (FileNotFoundException)
			{
				Environment.Exit(Errno.ENOENT);
			}
		}
	}
}
