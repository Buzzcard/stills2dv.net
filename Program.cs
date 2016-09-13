using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Stills2DV
{
	internal class Program
	{
		private const int BytesPerPixel = 4;

		private const string Usage = "stills2dv [-tmpdir <temp dir>] [-showoutput] [-fastrender ^ -nomotionblur] <scriptfile>\n";

		private static Image _cachedImage;
		private static Pixel _defaultColor = Pixel.FromColor(Color.Black);
		private static bool _fastRender;
		private static int _frameCount;
		private static float _framerate = 30.00f;
		private static Image _image;
		private static string _lastFilename;
		private static Movement _lastMovement;
		private static int _lastCropX;
		private static int _lastCropY;
		private static int _lastResizeX = -1;
		private static int _lastResizeY;
		private static int _lastSerial = -1;
		private static float _lastX;
		private static float _lastY;
		private static float _lastZ;
		private static bool _motionBlur = true;
		private static Movement _movement;
		private static int _jpegQuality = 90;
		private static OutputFormat _outputFormat = OutputFormat.PPM;
		private static int _outputHeight = 480;
		private static int _outputWidth = 720;
		private static double _panSmoothRatio = 0.2;
		private static Point[] _prev;
		private static int _serialIndex;
		private static int _sharpness = 1;
		private static bool _showOutput;
		private static string _tmpdir;
		private static double _zoomSmoothRatio = 0.2;

		private static void Action(ref Movement movement)
		{
			var icount = (int) (movement.Duration * _framerate);
			if (icount < 1)
				icount = 1;

			double fStartx = movement.StartX;
			double fStarty = movement.StartY;
			double fEndx = movement.EndX;
			double fEndy = movement.EndY;
			var x = fStartx;
			var y = fStarty;
			double zoom = movement.ZoomStart;
			for (var i = 0; i < icount; i++)
			{
				x += Smoothing.SmoothedStep(fStartx, fEndx, i, icount, _panSmoothRatio);
				y += Smoothing.SmoothedStep(fStarty, fEndy, i, icount, _panSmoothRatio);
				zoom += Smoothing.SmoothedStep(movement.ZoomStart, movement.ZoomEnd, i, icount, _zoomSmoothRatio);
				Frame(ref movement, (float) x, (float) y, (float) zoom);
			}
		}

		private static void ClearPrevBuffer()
		{
			for (var i = 0; i < _prev.Length; i++)
				_prev[i] = new Point((int) Position.Null, (int) Position.Null);
		}

		private static void CheckPrevBuffer(int width, int height)
		{
			var size = width * height;
			if (_prev != null && _prev.Length >= size)
				return;

			_prev = new Point[size];
			ClearPrevBuffer();
		}

		private static void Frame(ref Movement movement, float x, float y, float z)
		{
			var crossdata = new Pixel[0];

			if (_lastResizeX == -1)
				_lastFilename = string.Empty;

			// Calculate Resize
			float fx = movement.Width;
			fx *= z;
			var resizeX = (int) fx;
			if (resizeX < _outputWidth)
				resizeX = _outputWidth;

			float fy = movement.Height;
			fy *= z;
			var resizeY = (int) fy;
			if (resizeY < _outputHeight)
				resizeY = _outputHeight;

			// Calculate Crop
			fx = x * z;
			var cropX = (int) fx;
			cropX -= _outputWidth / 2;
			if (cropX < 0)
				cropX = 0;

			fy = y * z;
			var cropY = (int) fy;
			cropY -= _outputHeight / 2;
			if (cropY < 0)
				cropY = 0;

			var path = string.Format("{0}{1}{2:D5}.{3}", _tmpdir, Path.DirectorySeparatorChar, _frameCount, _outputFormat.ToString().ToLowerInvariant());

			_image = OpenImage(movement.Filename, true);

			Console.Write(@"Creating frame #{0} at x={1:0.00} y={2:0.00} and zoom {3:0.00}", _frameCount, x, y, z);
			var stopWatch = new Stopwatch();
			stopWatch.Start();
			var ppm = _fastRender
				? ResizeFast(_image, x, y, z)
				: Resize(_image, x, y, z);
			stopWatch.Stop();
			Console.WriteLine(@" {0}", stopWatch.ElapsedMilliseconds);

			if (movement.Crossfade > 0)
			{
				var crossedjpg = OpenImage(path, false);
				crossdata = crossedjpg.Pixels;
			}

			if (crossdata.Length > 0)
			{
				var bytecount = _outputWidth * _outputHeight;
				float ratioA = movement.Crossfade;
				float ratioB = movement.CrossFrames;

				ratioA /= ratioB;
				ratioB = 1 - ratioA;

				for (var i = 0; i < bytecount; i++)
				{
					var valueA = crossdata[i];
					var valueB = ppm[i];
					var mixedvalue = ppm[i];
					mixedvalue.R = (byte) (valueA.R * ratioA + valueB.R * ratioB);
					mixedvalue.G = (byte) (valueA.G * ratioA + valueB.G * ratioB);
					mixedvalue.B = (byte) (valueA.B * ratioA + valueB.B * ratioB);
					ppm[i] = mixedvalue;
				}
				movement.Crossfade--;
			}
			else
			{
				movement.Crossfade = 0;
				movement.CrossFrames = 0;
			}

			var res = new Image
			{
				Height = _outputHeight,
				Path = path,
				Pixels = ppm,
				Width = _outputWidth
			};

			if (_showOutput)
				ShowImage(res);

			if (_outputFormat == OutputFormat.JPG)
			{
				WriteJPG(res, path);
			}
			else
			{
				PPM.Write(res, path);
			}

			if (!movement.Filename.Equals(_lastFilename, StringComparison.Ordinal)
			    || _lastResizeX != resizeX
			    || _lastResizeY != resizeY
			    || _lastCropX != cropX
			    || _lastCropY != cropY)
			{
				_lastFilename = movement.Filename;
				_lastResizeX = resizeX;
				_lastResizeY = resizeY;
				_lastCropX = cropX;
				_lastCropY = cropY;
			}
			_frameCount++;
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
						case @"-fastrender":
							Console.WriteLine(@"Setting fastrender to true!");
							_fastRender = true;
							break;

						case @"-nomotionblur":
							Console.WriteLine(@"Setting fastrender to false!");
							_motionBlur = false;
							break;

						case @"-showoutput":
							Console.WriteLine(@"Setting showouput to true!");
							_showOutput = true;
							break;

						case @"-tmpdir":
							_tmpdir = args[i + 1];
							i++;
							break;

						default:
							Console.WriteLine(@"Unknown command: {0}", arg);
							Console.WriteLine(@"Usage: {0}", Usage);
							Environment.Exit(Errno.EPERM);
							return;
					}
				}
				else
				{
					if (fn != null)
					{
						Console.WriteLine(@"Usage: {0}", Usage);
						Environment.Exit(Errno.EPERM);
					}

					fn = arg;
				}
			}

			if (string.IsNullOrWhiteSpace(fn))
			{
				Console.WriteLine(@"Usage: {0}", Usage);
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

					if (ProcessArgs(ref _movement, splitted))
						Action(ref _movement);
				}
			}
			catch (FileNotFoundException)
			{
				Environment.Exit(Errno.ENOENT);
			}
		}

		private static void MotionBlur(ref Image image, ref float red, ref float green, ref float blue, ref float totalWeight, int x1, int y1, int x2, int y2)
		{
			float r, g, b;

			if (x2 == (int) Position.Null)
				return;

			var midX = (x1 + x2) / 2;
			var midY = (y1 + y2) / 2;

			if ((midX == x1) && (midY == y1))
				return;

			if ((midX == x2) && (midY == y2))
				return;

			MotionBlur(ref image, ref red, ref green, ref blue, ref totalWeight, x1, y1, midX, midY);
			MotionBlur(ref image, ref red, ref green, ref blue, ref totalWeight, x2, y2, midX, midY);

			if ((midX >= 0) && (midY >= 0) && (midX < image.Width) && (midY < image.Height))
			{
				var inOffset = midY * image.Width + midX;
				r = image.Pixels[inOffset].R;
				g = image.Pixels[inOffset].G;
				b = image.Pixels[inOffset].B;
			}
			else
			{
				r = _defaultColor.R;
				g = _defaultColor.G;
				b = _defaultColor.B;
			}

			red += r;
			green += g;
			blue += b;
			totalWeight += 1.0f;
		}

		private static void Normalize(ref Movement movement)
		{
			float oresx = _outputWidth;
			float oresy = _outputHeight;
			float iresx = movement.Width;
			float iresy = movement.Height;
			var fitheightzoom = oresy * iresx / (oresx * iresy);

			var fitzoom = fitheightzoom < 1.0
				? fitheightzoom
				: 1f;

			if (movement.ZoomEndCode == ZoomCode.FitHeight)
				movement.ZoomEnd = fitheightzoom;

			if (movement.ZoomEndCode == ZoomCode.FitWidth)
				movement.ZoomEnd = 1f;

			if (movement.ZoomEndCode == ZoomCode.Fit)
				movement.ZoomEnd = fitzoom;

			if (movement.ZoomStartCode == ZoomCode.FitHeight)
				movement.ZoomStart = fitheightzoom;

			if (movement.ZoomStartCode == ZoomCode.FitWidth)
				movement.ZoomStart = 1f;

			if (movement.ZoomStartCode == ZoomCode.Fit)
				movement.ZoomStart = fitzoom;

			// ReSharper disable once SwitchStatementMissingSomeCases
			switch (movement.StartX)
			{
				case (int) Position.Center:
					movement.StartX = movement.Width / 2;
					movement.StartY = movement.Height / 2;
					break;

				case (int) Position.Left:
					movement.StartX = (int) (iresx / (movement.ZoomStart * 2));
					movement.StartY = movement.Height / 2;
					break;

				case (int) Position.Right:
					movement.StartX = (int) (iresx - iresx / (movement.ZoomStart * 2));
					movement.StartY = movement.Height / 2;
					break;

				case (int) Position.Bottom:
					movement.StartX = movement.Width / 2;
					movement.StartY = (int) (iresy - iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.Top:
					movement.StartX = movement.Width / 2;
					movement.StartY = (int) (iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.TopRight:
					movement.StartX = (int) (iresx - iresx / (movement.ZoomStart * 2));
					movement.StartY = (int) (iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.TopLeft:
					movement.StartX = (int) (iresx / (movement.ZoomStart * 2));
					movement.StartY = (int) (iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.BottomRight:
					movement.StartX = (int) (iresx - iresx / (movement.ZoomStart * 2));
					movement.StartY = (int) (iresy - iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.BottomLeft:
					movement.StartX = (int) (iresx / (movement.ZoomStart * 2));
					movement.StartY = (int) (iresy - iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;
			}

			// ReSharper disable once SwitchStatementMissingSomeCases
			switch (movement.EndX)
			{
				case (int) Position.Center:
					movement.EndX = movement.Width / 2;
					movement.EndY = movement.Height / 2;
					break;

				case (int) Position.Left:
					movement.EndX = (int) (iresx / (movement.ZoomEnd * 2));
					movement.EndY = movement.Height / 2;
					break;

				case (int) Position.Right:
					movement.EndX = (int) (iresx - iresx / (movement.ZoomEnd * 2));
					movement.EndY = movement.Height / 2;
					break;

				case (int) Position.Bottom:
					movement.EndX = movement.Width / 2;
					movement.EndY = (int) (iresy - iresx * oresy / (movement.ZoomEnd * 2 * oresx));
					break;

				case (int) Position.Top:
					movement.EndX = movement.Width / 2;
					movement.EndY = (int) (iresx * oresy / (movement.ZoomEnd * 2 * oresx));
					break;

				case (int) Position.TopRight:
					movement.EndX = (int) (iresx - iresx / (movement.ZoomStart * 2));
					movement.EndY = (int) (iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.TopLeft:
					movement.EndX = (int) (iresx / (movement.ZoomStart * 2));
					movement.EndY = (int) (iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.BottomRight:
					movement.EndX = (int) (iresx - iresx / (movement.ZoomStart * 2));
					movement.EndY = (int) (iresy - iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;

				case (int) Position.BottomLeft:
					movement.EndX = (int) (iresx / (movement.ZoomStart * 2));
					movement.EndY = (int) (iresy - iresx * oresy / (movement.ZoomStart * 2 * oresx));
					break;
			}
		}

		private static Image OpenImage(string path, bool cache)
		{
			if (_cachedImage.Path != null && _cachedImage.Path.Equals(path))
				return _cachedImage;

			var len = path.Length;
			if (len < 5)
				throw new ArgumentException(@"path is too short.", @"path");

			var ext = Path.GetExtension(path);

			var image = ext.EndsWith(@"ppm", StringComparison.OrdinalIgnoreCase)
				? PPM.Read(path)
				: ReadImage(path);

			image.Serial = _serialIndex++;

			if (cache)
				_cachedImage = image;

			return image;
		}


		private static bool ProcessArgs(ref Movement movement, IList<string> strs)
		{
			if (strs.Count == 0)
				return false;

			movement.EndX = (int) Position.Center;
			movement.EndY = (int) Position.Center;
			movement.Filename = null;
			movement.Height = 0;
			movement.SmoothEnd = 0f;
			movement.SmoothStart = 0f;
			movement.StartX = (int) Position.Center;
			movement.StartY = (int) Position.Center;
			movement.Width = 0;
			movement.ZoomEnd = 1f;
			movement.ZoomEndCode = (int) ZoomCode.None;
			movement.ZoomStart = 1f;
			movement.ZoomStartCode = (int) ZoomCode.None;

			var actiondata = false;
			var continued = false;

			for (var i = 0; i < strs.Count; i++)
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
						if (i == strs.Count)
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
						if (i == strs.Count)
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
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (!int.TryParse(str, out h) || h <= 0)
							continue;

						_outputHeight = h;
						break;

					case @"sharpness":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						int s;
						if (!int.TryParse(str, out s) || s <= 0)
							continue;

						_sharpness = s;
						break;

					case @"pansmoothness":
						if (i == strs.Count)
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
						if (i == strs.Count)
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
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (!int.TryParse(str, out h) || h <= 0)
							continue;

						_jpegQuality = h;
						break;

					case @"fps":
					case @"framerate":
						if (i == strs.Count)
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
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (str.Equals(@"jpeg", StringComparison.OrdinalIgnoreCase) || str.Equals(@"jpg", StringComparison.OrdinalIgnoreCase))
							_outputFormat = OutputFormat.JPG;
						else if (str.Equals(@"ppm", StringComparison.OrdinalIgnoreCase))
							_outputFormat = OutputFormat.PPM;

						break;

					case @"backgroundcolor":
					case @"bgcolor":
					case @"defaultcolor":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i].Replace(@"#", string.Empty);

						int c;
						if (!int.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture.NumberFormat, out c))
							continue;

						_defaultColor = Pixel.FromArgb(c);
						break;

					case @"img":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						movement.Filename = str;

						_image = OpenImage(str, true);

						movement.Height = _image.Height;
						movement.Width = _image.Width;
						actiondata = true;
						break;

					case @"crossfade":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						movement.Duration = f;
						movement.Crossfade = (int) (f * _framerate) - 2;

						if (_frameCount < movement.Crossfade)
							movement.Crossfade = _frameCount;

						_frameCount -= movement.Crossfade;
						movement.CrossFrames = movement.Crossfade;
						break;

					case @"startpoint":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						location = str.Split(',');
						if (!int.TryParse(location[0], out x) || x < 0)
						{
							movement.EndX = movement.EndY = movement.StartX = movement.StartY = Translate(location[0]);
							continue;
						}

						if (!int.TryParse(location[1], out y) || y < 0)
							continue;

						movement.StartX = x;
						movement.EndX = x;

						movement.StartY = y;
						movement.EndY = y;

						actiondata = true;
						break;

					case @"endpoint":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						location = str.Split(',');
						if (!int.TryParse(location[0], out x) || x < 0)
						{
							movement.EndX = movement.EndY = Translate(location[0]);
							continue;
						}

						if (!int.TryParse(location[1], out y) || y < 0)
							continue;

						movement.EndX = x;
						movement.EndY = y;

						actiondata = true;
						break;

					case @"zoom":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						var zoom = str.Split(',');
						if (zoom.Length == 2)
						{
							if (float.TryParse(zoom[0], out f))
							{
								movement.ZoomStart = f;
							}
							else
							{
								movement.ZoomStart = 0;
								movement.ZoomStartCode = (ZoomCode) Translate(zoom[0]);
							}

							if (float.TryParse(zoom[1], out f))
							{
								movement.ZoomEnd = f;
							}
							else
							{
								movement.ZoomEnd = 0;
								movement.ZoomEndCode = (ZoomCode) Translate(zoom[1]);
							}
						}
						else
						{
							if (continued)
							{
								if (float.TryParse(zoom[0], out f))
								{
									movement.ZoomEnd = f;
								}
								else
								{
									movement.ZoomEnd = 0;
									movement.ZoomEndCode = (ZoomCode) Translate(zoom[0]);
								}
							}
							else
							{
								if (float.TryParse(zoom[0], out f))
								{
									movement.ZoomStart = f;
									movement.ZoomEnd = f;
								}
								else
								{
									movement.ZoomStart = 0;
									movement.ZoomEnd = 0;
									var code = Translate(zoom[0]);
									movement.ZoomEndCode = (ZoomCode) code;
									movement.ZoomStartCode = (ZoomCode) code;
								}
							}
						}
						actiondata = true;
						break;

					case @"duration":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						movement.Duration = f;
						actiondata = true;
						break;

					case @"startsmooth":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						movement.SmoothStart = f;
						actiondata = true;
						break;

					case @"endsmooth":
						if (i == strs.Count)
							continue;

						i++;
						str = strs[i];

						if (!float.TryParse(str, out f) || f <= 0)
							continue;

						movement.SmoothEnd = f;
						actiondata = true;
						break;

					case @"continue":
						continued = true;
						movement.EndX = movement.StartX = _lastMovement.EndX;
						movement.EndY = movement.StartY = _lastMovement.EndY;
						movement.Filename = _lastMovement.Filename;
						movement.Height = _lastMovement.Height;
						movement.Width = _lastMovement.Width;
						movement.ZoomEnd = movement.ZoomStart = _lastMovement.ZoomEnd;
						actiondata = true;
						break;

					default:
						Console.WriteLine("Unknown command: {0}", str);
						break;
				}
			}

			if (actiondata)
			{
				Normalize(ref movement);
				_lastMovement = movement;
				return true;
			}
			return false;
		}

		private static Image ReadImage(string path)
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


		private static Pixel[] Resize(Image image, float x, float y, float z)
		{
			if (_lastSerial != image.Serial)
			{
				_lastX = x;
				_lastY = y;
				_lastZ = z;

				_lastSerial = image.Serial;
				return ResizeNoBlur(image, x, y, z);
			}

			if ((_lastX == x) && (_lastY == y) && (_lastZ == z))
				return ResizeNoBlur(image, x, y, z);

			var bufsize = _outputWidth * _outputHeight;
			var res = new Pixel[bufsize];
			for (var i = 0; i < bufsize; i++)
				res[i] = _defaultColor;

			float fw = image.Width;
			float frw = _outputWidth;
			float frh = _outputHeight;

			var aaRadius = fw / (frw * z * _sharpness);
			if (aaRadius < 1.33333)
				aaRadius = 1.33333f;

			var aaRadiusSquared = aaRadius * aaRadius;

			CheckPrevBuffer(_outputWidth, _outputHeight);
			var pprev = 0;

			for (var iy = 0; iy < _outputHeight; iy++)
			{
				float fry = iy;
				var deltaY = y - fw * frh / (z * 2.0f * frw) + fry * fw / (frw * z);
				var fstarty = deltaY - aaRadius;
				var istarty = (int) fstarty;
				var fendy = deltaY + aaRadius;
				var iendy = (int) fendy;

				for (var ix = 0; ix < _outputWidth; ix++)
				{
					float frx = ix;
					var deltaX = x - fw / (z * 2.0f) + frx * fw / (frw * z);
					var fstartx = deltaX - aaRadius;
					var fendx = deltaX + aaRadius;
					var istartx = (int) fstartx;
					var iendx = (int) fendx;

					var fred = 0f;
					var fgreen = 0f;
					var fblue = 0f;
					var ftotalweight = 0f;

					for (var i = istarty; i <= iendy; i++)
					{
						if ((i < 0) || (i >= image.Height))
							continue;

						float fi = i;

						for (var j = istartx; j <= iendx; j++)
						{
							if ((j < 0) || (j >= image.Width))
								continue;

							float fj = j;

							var angularDistanceSquared = (fj - deltaX) * (fj - deltaX) + (fi - deltaY) * (fi - deltaY);
							if (aaRadiusSquared - angularDistanceSquared <= 0.0)
								continue;

							var angularDistance = (float) Math.Sqrt(angularDistanceSquared);
							//if (aaRadius - angularDistance <= 0.0)
							//	continue;

							var fweight = aaRadius - angularDistance > 0.001
								? aaRadius - angularDistance
								: 0.001f;

							ftotalweight += fweight;

							var inOffset = i * image.Width + j;
							var inputPixel = image.Pixels[inOffset];
							fred += inputPixel.R * fweight;
							fgreen += inputPixel.G * fweight;
							fblue += inputPixel.B * fweight;
						}
					}

					if (ftotalweight > 0f)
					{
						fred /= ftotalweight;
						fgreen /= ftotalweight;
						fblue /= ftotalweight;

						if (_motionBlur)
						{
							ftotalweight = 1.0f;
							istartx = (int) deltaX;
							istarty = (int) deltaY;

							MotionBlur(ref image, ref fred, ref fgreen, ref fblue, ref ftotalweight, istartx, istarty, _prev[pprev].X, _prev[pprev].Y);

							fred /= ftotalweight;
							fgreen /= ftotalweight;
							fblue /= ftotalweight;
							_prev[pprev] = new Point(istartx, istarty);
						}

						var outOffset = iy * _outputWidth + ix;
						var outputPixel = res[outOffset];
						outputPixel.R = (byte) (int) fred;
						outputPixel.G = (byte) (int) fgreen;
						outputPixel.B = (byte) (int) fblue;
						res[outOffset] = outputPixel;
					}

					if (_motionBlur)
					{
						pprev++;
					}
				}
			}
			return res;
		}

		private static Pixel[] ResizeFast(Image image, float x, float y, float z)
		{
			var bufsize = _outputWidth * _outputHeight;
			var res = new Pixel[bufsize];

			float fw = image.Width;
			float frw = _outputWidth;
			float frh = _outputHeight;

			//for (var iy = 0; iy < _outputHeight; iy++)
			Parallel.For(0, _outputHeight, iy =>
			{
				float fry = iy;
				var deltaY = y - fw * frh / (z * 2f * frw) + fry * fw / (frw * z);
				var i = (int) deltaY;

				for (var ix = 0; ix < _outputWidth; ix++)
				{
					float frx = ix;
					var deltaX = x - fw / (z * 2f) + frx * fw / (frw * z);
					var j = (int) deltaX;

					var inOffset = i * image.Width + j;
					var outOffset = iy * _outputWidth + ix;
					res[outOffset] = (i >= 0) && (i < image.Height) && (j >= 0) && (j < image.Width)
						? image.Pixels[inOffset]
						: _defaultColor;
				}
			});
			return res;
		}

		private static Pixel[] ResizeNoBlur(Image image, float x, float y, float z)
		{
			var bufsize = _outputWidth * _outputHeight;
			var res = new Pixel[bufsize];
			for (var i = 0; i < bufsize; i++)
				res[i] = _defaultColor;

			float fw = image.Width;
			float frw = _outputWidth;
			float frh = _outputHeight;

			var aaRadius = fw / (frw * z * _sharpness);
			if (aaRadius < 1.33333)
				aaRadius = 1.33333f;

			var aaRadiusSquared = aaRadius * aaRadius;

			CheckPrevBuffer(_outputWidth, _outputHeight);
			var pprev = 0;

			for (var iy = 0; iy < _outputHeight; iy++)
			{
				float fry = iy;
				var deltaY = y - fw * frh / (z * 2.0f * frw) + fry * fw / (frw * z);
				var fstarty = deltaY - aaRadius;
				var fendy = deltaY + aaRadius;
				var istarty = (int) fstarty;
				var iendy = (int) fendy;

				for (var ix = 0; ix < _outputWidth; ix++)
				{
					float frx = ix;
					var deltaX = x - fw / (z * 2.0f) + frx * fw / (frw * z);
					var fstartx = deltaX - aaRadius;
					var fendx = deltaX + aaRadius;
					var istartx = (int) fstartx;
					var iendx = (int) fendx;

					if (_motionBlur)
					{
						_prev[pprev] = new Point((int) deltaX, (int) deltaY);
						pprev++;
					}

					var fred = 0f;
					var fgreen = 0f;
					var fblue = 0f;
					var ftotalweight = 0f;

					for (var i = istarty; i <= iendy; i++)
					{
						if ((i < 0) || (i >= image.Height))
							continue;

						float fi = i;

						for (var j = istartx; j <= iendx; j++)
						{
							if ((j < 0) || (j >= image.Width))
								continue;

							float fj = j;

							var angularDistanceSquared = (fj - deltaX) * (fj - deltaX) + (fi - deltaY) * (fi - deltaY);
							if (aaRadiusSquared - angularDistanceSquared <= 0.0)
								continue;

							var angularDistance = (float) Math.Sqrt(angularDistanceSquared);
							//if (aaRadius - angularDistance <= 0.0)
							//	continue;

							var fweight = aaRadius - angularDistance > 0.001
								? aaRadius - angularDistance
								: 0.001f;

							ftotalweight = ftotalweight + fweight;

							var inOffset = i * image.Width + j;
							var inputPixel = image.Pixels[inOffset];

							fred += inputPixel.R * fweight;
							fgreen += inputPixel.G * fweight;
							fblue += inputPixel.B * fweight;
						}
					}

					if (ftotalweight <= 0.0)
						continue;

					fred /= ftotalweight;
					fgreen /= ftotalweight;
					fblue /= ftotalweight;

					var outOffset = iy * _outputWidth + ix;
					var outputPixel = res[outOffset];
					outputPixel.R = (byte) (int) fred;
					outputPixel.G = (byte) (int) fgreen;
					outputPixel.B = (byte) (int) fblue;
					res[outOffset] = outputPixel;
				}
			}
			return res;
		}

		private static void ShowImage(Image image)
		{
			throw new NotImplementedException();
		}

		private static string[] Split(string ligne)
		{
			return Regex.Split(ligne, @"\s");
		}

		private static int Translate(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentNullException(@"s");

			switch (value.ToLowerInvariant())
			{
				case @"center":
					return (int) Position.Center;

				case @"left":
					return (int) Position.Left;

				case @"right":
					return (int) Position.Right;

				case @"top":
					return (int) Position.Top;

				case @"bottom":
					return (int) Position.Bottom;

				case @"fitwidth":
					return (int) ZoomCode.FitWidth;

				case @"fitheight":
					return (int) ZoomCode.FitHeight;

				case @"fit":
					return (int) ZoomCode.Fit;

				case @"topright":
				case @"righttop":
					return (int) Position.TopRight;

				case @"lefttop":
				case @"topleft":
					return (int) Position.TopLeft;

				case @"bottomright":
				case @"rightbottom":
					return (int) Position.BottomRight;

				case @"leftbottom":
				case @"bottomleft":
					return (int) Position.BottomLeft;

				default:
					return 0;
			}
		}

		private static void Write(Image image, string path, ImageCodecInfo encoder = null, EncoderParameters encoderParameters = null)
		{
			var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);

			var pixels = new byte[image.Width * image.Height * BytesPerPixel];
			using (var pixelStream = new MemoryStream(pixels, true))
			using (var writer = new BinaryWriter(pixelStream))
			{
				foreach (var color in image.Pixels)
					writer.Write(color.ToArgb());

				writer.Flush();

				var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
				var ptr = bitmapData.Scan0;
				Marshal.Copy(pixels, 0, ptr, pixels.Length);
				bitmap.UnlockBits(bitmapData);
			}

			var dir = Path.GetDirectoryName(path);
			if (dir != null && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			if (encoder == null)
				bitmap.Save(path);
			else
				bitmap.Save(path, encoder, encoderParameters);
		}

		private static void WriteJPG(Image image, string path)
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
			Write(image, path, jpgEncoder, encoderParameters);
		}
	}
}