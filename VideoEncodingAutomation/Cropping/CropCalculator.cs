using BPUtil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoEncodingAutomation.Cropping
{
	class QueuedImage { public double progress; public byte[] imageFileData; }
	public class CropCalculator
	{
		/// <summary>
		/// If set to true, the CropCalculator will try to abort any running operation.
		/// </summary>
		public volatile bool abort = false;
		private bool isDone = false;
		private string videoFilePath;
		private Action<double, ImageFile> newFrameCallback;
		private bool debugFrames = false;
		private ConcurrentQueue<QueuedImage> newImageQueue = new ConcurrentQueue<QueuedImage>();
		private VideoThumbnailer vt = null;
		/// <summary>
		/// Contains the exception that caused the CropCalculations thread to abort. Typically null.
		/// </summary>
		private Exception exceptionCaught = null;
		/// <summary>
		/// Pool of byte arrays for the sake of efficiency when copying bitmap image data to managed memory.
		/// </summary>
		private ObjectPool<byte[]> poolRawData = new ObjectPool<byte[]>(() => new byte[0]);

		/// <summary>
		/// The calculated rectangle.
		/// </summary>
		public CropRectangle rect { get; private set; } = CropRectangle.InitialValue();

		//public static ConcurrentQueue<double> qlight = new ConcurrentQueue<double>();
		//public static ConcurrentQueue<double> qdarkC = new ConcurrentQueue<double>();
		//public static ConcurrentQueue<double> qdarkB = new ConcurrentQueue<double>();
		//public static ConcurrentQueue<double> qdarkA = new ConcurrentQueue<double>();

		/// <summary>
		/// 
		/// </summary>
		/// <param name="videoFilePath">Path to a video file that may contain black bars.</param>
		/// <param name="newFrameCallback">A callback that is called with each frame and the percentage progress.  You must dispose all ImageFile instances received via this callback when you are done with them.  Note that sometimes the ImageFile is null, so obviously there is nothing to dispose in that case.</param>
		/// <param name="debugFrames">If true, frames that yield a crop calculation change will be annotated and saved to disk in the current directory.</param>
		public CropCalculator(string videoFilePath, Action<double, ImageFile> newFrameCallback, bool debugFrames)
		{
			this.videoFilePath = videoFilePath;
			this.newFrameCallback = newFrameCallback;
			this.debugFrames = debugFrames;
		}

		public CropRectangle CalculateCrop()
		{
			if (vt != null)
				throw new Exception("Cannot reuse a CropCalculator instance.");

			vt = new VideoThumbnailer(videoFilePath);

			Thread thrCropCalculations = new Thread(CropCalculationsMethod);
			thrCropCalculations.IsBackground = false;
			thrCropCalculations.Name = "CropCalculations";
			try
			{
				thrCropCalculations.Start();
				vt.GetThumbnailsEvenlyDispersed(20, 60, true, (byte)(Environment.ProcessorCount / 3), ThumbnailProgress);
				isDone = true;
				thrCropCalculations.Join();
			}
			finally
			{
				newImageQueue = new ConcurrentQueue<QueuedImage>();
				try
				{
					if (thrCropCalculations.IsAlive)
						thrCropCalculations.Abort();
				}
				catch { }
			}
			if (exceptionCaught != null)
				throw exceptionCaught;
			//File.WriteAllText("light.txt", string.Join(" ", qlight.Select(v => v.ToString("0.0000"))));
			//File.WriteAllText("darkC.txt", string.Join(" ", qdarkC.Select(v => v.ToString("0.0000"))));
			//File.WriteAllText("darkB.txt", string.Join(" ", qdarkB.Select(v => v.ToString("0.0000"))));
			//File.WriteAllText("darkA.txt", string.Join(" ", qdarkA.Select(v => v.ToString("0.0000"))));
			if (rect.IsValid())
			{
				rect.InflateToModulus2();
				return rect;
			}
			else
				return null;
		}
		private void ThumbnailProgress(double progress, byte[] imageFileData)
		{
			newImageQueue.Enqueue(new QueuedImage() { progress = progress, imageFileData = imageFileData });
		}
		long imageCounter = 0;
		private void CropCalculationsMethod()
		{
			try
			{
				while (true)
				{
					if (abort)
					{
						vt.abort = true;
						return;
					}
					else if (newImageQueue.TryDequeue(out QueuedImage q))
					{
						if (q.imageFileData == null)
							newFrameCallback(q.progress, null);
						else
						{
							ImageFile img = new ImageFile(q.imageFileData);
							if (Interlocked.Increment(ref imageCounter) == 1)
							{
								rect.SourceWidth = img.Width;
								rect.SourceHeight = img.Height;
							}
							if (img.img.PixelFormat != PixelFormat.Format24bppRgb)
							{
								Bitmap clone = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
								try
								{
									using (Graphics gr = Graphics.FromImage(clone))
									{
										gr.DrawImage(img.img, new Rectangle(0, 0, clone.Width, clone.Height));
									}
								}
								finally
								{
									img.img.Dispose();
									img.img = clone;
								}
							}
							DetermineCropSize((Bitmap)img.img);
							if (rect.Top == 0 && rect.Left == 0 && rect.Bottom == img.Height - 1 && rect.Right == img.Width - 1)
							{
								// We've determined that no cropping is needed.
								vt.abort = true;
								abort = true;
							}

							newFrameCallback(q.progress, img);
						}
					}
					else if (isDone)
						return;
					else
						Thread.Sleep(1);
				}
			}
			catch (ThreadAbortException) { return; }
			catch (Exception ex)
			{
				exceptionCaught = ex;
			}
			finally
			{
				vt.abort = true;
			}
		}
		/// <summary>
		/// Expands the current crop rectangle as needed based on the image's black bars.
		/// </summary>
		/// <param name="bmp">Bitmap for which to determine crop size.</param>
		/// <returns></returns>
		private void DetermineCropSize(Bitmap bmp)
		{
			if (bmp.PixelFormat != PixelFormat.Format24bppRgb)
				throw new ApplicationException("CropCalculator does not understand pixel format: " + bmp.PixelFormat);
			bool adjusted = false;
			BitmapData bitmapData = null;
			byte[] rawData = null;
			try
			{
				bitmapData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), debugFrames ? ImageLockMode.ReadWrite : ImageLockMode.ReadOnly, bmp.PixelFormat);
				int dataLength = bitmapData.Stride * bitmapData.Height;
				rawData = poolRawData.GetObject(() => new byte[dataLength]);
				if (rawData.Length != dataLength)
					throw new Exception("CropCalculator found inconsistent frame size in input video. A frame was " + bmp.Width + "x" + bmp.Height + " which does not match the expectation (" + rect.SourceWidth + "x" + rect.SourceHeight + ").");
				Marshal.Copy(bitmapData.Scan0, rawData, 0, rawData.Length);
				// Scan down from top
				for (int y = 0; y < bmp.Height && y < rect.Top; y++)
				{
					if (ScanRow(y, rawData, bmp.Width, bmp.Height, bitmapData.Stride))
					{
						adjusted = true;
						//Console.WriteLine("rect.Top = " + y);
						rect.Top = y;
					}
				}
				// Scan right from left
				for (int x = 0; x < bmp.Width && x < rect.Left; x++)
				{
					if (ScanColumn(x, rawData, bmp.Width, bmp.Height, bitmapData.Stride))
					{
						adjusted = true;
						//Console.WriteLine("rect.Left = " + x);
						rect.Left = x;
					}
				}
				// Scan left from right
				for (int x = bmp.Width - 1; x >= 0 && x > rect.Right; x--)
				{
					if (ScanColumn(x, rawData, bmp.Width, bmp.Height, bitmapData.Stride))
					{
						adjusted = true;
						//Console.WriteLine("rect.Right = " + x);
						rect.Right = x;
					}
				}
				// Scan up from bottom
				for (int y = bmp.Height - 1; y >= 0 && y > rect.Bottom; y--)
				{
					if (ScanRow(y, rawData, bmp.Width, bmp.Height, bitmapData.Stride))
					{
						adjusted = true;
						//Console.WriteLine("rect.Bottom = " + y);
						rect.Bottom = y;
					}
				}
			}
			finally
			{
				int frame = Interlocked.Increment(ref adjustCounter);
				if (adjusted && debugFrames)
				{
					bmp.Save(frame.ToString().PadLeft(5, '0') + "-src.png", ImageFormat.Png);
					MarkRow(rect.Top, rawData, rect.SourceWidth, rect.SourceHeight, bitmapData.Stride);
					MarkRow(rect.Bottom, rawData, rect.SourceWidth, rect.SourceHeight, bitmapData.Stride);
					MarkColumn(rect.Left, rawData, rect.SourceWidth, rect.SourceHeight, bitmapData.Stride);
					MarkColumn(rect.Right, rawData, rect.SourceWidth, rect.SourceHeight, bitmapData.Stride);
					Marshal.Copy(rawData, 0, bitmapData.Scan0, rawData.Length);
				}
				bmp?.UnlockBits(bitmapData);
				if (adjusted && debugFrames)
					bmp.Save(frame.ToString().PadLeft(5, '0') + "-dbg.png", ImageFormat.Png);
			}
		}
		private int adjustCounter = 0;
		/// <summary>
		/// Returns true the row is determined to contain meaningful image data based on the judgement of PixelSet.
		/// </summary>
		/// <param name="y">row index</param>
		/// <param name="rawData">raw image data, 3 bits per pixel</param>
		/// <param name="width">image width</param>
		/// <param name="height">image height</param>
		/// <param name="stride">bitmap stride (bytes per row)</param>
		/// <returns></returns>
		public bool ScanRow(int y, byte[] rawData, int width, int height, int stride)
		{
			PixelSet set = new PixelSet();
			int start = y * stride;
			int end = start + (width * 3);
			for (int bo = start; bo < end; bo += 3)
			{
				set.AddPixel(GetBrightnessOfPixelBGR(rawData, bo));
			}
			return set.JudgeIsMeaningful();
		}
		/// <summary>
		/// Returns true the column is determined to contain meaningful image data based on the judgement of PixelSet.
		/// </summary>
		/// <param name="x">column index</param>
		/// <param name="rawData">raw image data, 3 bits per pixel</param>
		/// <param name="width">image width</param>
		/// <param name="height">image height</param>
		/// <param name="stride">bitmap stride (bytes per row)</param>
		/// <returns></returns>
		public bool ScanColumn(int x, byte[] rawData, int width, int height, int stride)
		{
			PixelSet set = new PixelSet();
			for (int y = 0; y < height; y++)
			{
				int bo = (y * stride) + (x * 3);
				set.AddPixel(GetBrightnessOfPixelBGR(rawData, bo));
			}
			return set.JudgeIsMeaningful();
		}
		private void MarkRow(int y, byte[] rawData, int width, int height, int stride)
		{
			int start = y * stride;
			int end = start + (width * 3);
			for (int bo = start; bo < end; bo += 3)
			{
				MarkPixel(rawData, bo);
			}
		}

		private void MarkColumn(int x, byte[] rawData, int width, int height, int stride)
		{
			for (int y = 0; y < height; y++)
			{
				int bo = (y * stride) + (x * 3);
				MarkPixel(rawData, bo);
			}
		}
		private void MarkPixel(byte[] buf, int idxStart)
		{
			float brightness = GetBrightnessOfPixelBGR(buf, idxStart);
			if (brightness == 0)
			{
			}
			else if (brightness < 0.01f) // If dark level A (effectively black)
			{
				buf[idxStart] = (byte)Math.Round(brightness * 200); // B
				buf[idxStart + 1] = 40; // G
				buf[idxStart + 2] = 40; // R
			}
			else if (brightness < 0.025f) // If dark level B (borderline black)
			{
				buf[idxStart] = (byte)Math.Round(brightness * 200); // B
				buf[idxStart + 1] = 100; // G
				buf[idxStart + 2] = 100; //R
			}
			else if (brightness < 0.05f) // If dark level C (very dark)
			{
				buf[idxStart] = (byte)Math.Round(brightness * 200); // B
				buf[idxStart + 1] = 170; // G
				buf[idxStart + 2] = 170; // R
			}
			else
			{
				buf[idxStart] = (byte)Math.Round(brightness * 200); // B
				buf[idxStart + 1] = 255;
				buf[idxStart + 2] = 255;
			}
		}
		private float GetBrightnessOfPixelBGR(byte[] buf, int idxStart)
		{
			return (buf[idxStart] / 255f) * 0.114f  // B
			 + (buf[idxStart + 1] / 255f) * 0.587f  // G
			 + (buf[idxStart + 2] / 255f) * 0.229f; // R
		}
		class PixelSet
		{
			public int darkA; // Effectively black
			public int darkB; // Borderline visible
			public int darkC; // Visible, but probably not important
			public int light; // Valuable
			public void AddPixel(float brightness)
			{
				if (brightness < 0.01f)
					darkA++;
				else if (brightness < 0.025f)
					darkB++;
				else if (brightness < 0.05f)
					darkC++;
				else
					light++;
			}
			public bool JudgeIsMeaningful()
			{
				double totalSubpixels = darkA + darkB + darkC + light;

				//if (light / totalSubpixels > 0)
				//	CropCalculator.qlight.Enqueue(light / totalSubpixels);
				//if (darkC / totalSubpixels > 0)
				//	CropCalculator.qdarkC.Enqueue(darkC / totalSubpixels);
				//if (darkB / totalSubpixels > 0)
				//	CropCalculator.qdarkB.Enqueue(darkB / totalSubpixels);
				//if (darkA / totalSubpixels < 1)
				//	CropCalculator.qdarkA.Enqueue(darkA / totalSubpixels);

				if (light / totalSubpixels > 0.001) // Minimum 0.1% light
				{
					//Console.WriteLine("light ratio " + (light / totalSubpixels));
					return true;
				}
				if (darkC / totalSubpixels > 0.02) // Minimum 2% dark level C
				{
					//Console.WriteLine("darkC ratio " + (light / totalSubpixels));
					return true;
				}
				if (darkB / totalSubpixels > 0.9) // Minimum 90% dark level B
				{
					//Console.WriteLine("darkB ratio " + (light / totalSubpixels));
					return true;
				}
				return false;
			}
		}
	}
}
