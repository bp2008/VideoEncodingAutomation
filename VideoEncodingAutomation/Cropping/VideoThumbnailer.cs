using BPUtil;
using QuickType;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VideoEncodingAutomation.Cropping
{
	/// <summary>
	/// <para>Creates thumbnails from a video file.</para>
	/// <para>Forked from VideoVerifier project which contains several alternate implementations.</para>
	/// </summary>
	public class VideoThumbnailer
	{
		/// <summary>
		/// If set to true, the VideoThumbnailer will try to abort any running operation.
		/// </summary>
		public volatile bool abort = false;

		public readonly FileInfo InputFile;
		public VideoThumbnailer(string inputFile)
		{
			InputFile = new FileInfo(inputFile);
		}
		/// <summary>
		/// Generates thumbnails, returning each via the [onProgress] callback method.
		/// </summary>
		/// <param name="captureInterval">
		/// <para>Desired frame capture interval in seconds.</para>
		/// <para>If this is less than 10, the keyframe-only filter will be disabled and decoding will probably be slower.</para>
		/// <para>Tip: A 10-second interval will deliver up to 360 frames per hour. A 60-second interval, only 60 frames per hour.</para>
		/// </param>
		/// <param name="minimumCaptures">Minimum number of snapshots to capture.  [captureInterval] is ignored if it would not produce at least this many snapshots.</param>
		/// <param name="losslessThumbnails">If true, thumbnails are saved in .png format so as to be lossless. If false, jpeg with ffmpeg-determined quality.</param>
		/// <param name="maxThreads">Maximum number of threads to run.</param>
		/// <param name="onProgress">Callback method that receives progress updates [0-1] and raw image data. The longer this callback takes to execute, the slower the thumbnailing process will be.</param>
		public void GetThumbnailsEvenlyDispersed(int captureInterval, int minimumCaptures, bool losslessThumbnails, byte maxThreads, Action<double, byte[]> onProgress)
		{
			if (maxThreads == 0)
				maxThreads = 1;

			if (!InputFile.Exists)
				throw new ApplicationException("Input file does not exist: \"" + InputFile + "\"");

			FileInfo ffmpeg = new FileInfo(Path.Combine("ffmpeg", "ffmpeg.exe"));
			if (!ffmpeg.Exists)
				throw new ApplicationException("ffmpeg.exe does not exist in the subfolder \"ffmpeg\". Please download ffmpeg and extract ffmpeg.exe here.");

			FileInfo ffprobe = new FileInfo(Path.Combine("ffmpeg", "ffprobe.exe"));
			if (!ffprobe.Exists)
				throw new ApplicationException("ffprobe.exe does not exist in the subfolder \"ffmpeg\". Please download ffmpeg and extract ffprobe.exe here.");

			double sourceVideoDurationSeconds = 0;

			Process maybeActiveProcess = null;
			try
			{
				// Get media info
				{
					StringBuilder sbOut = new StringBuilder();
					StringBuilder sbErr = new StringBuilder();
					ProcessRunnerHandle ffprobeHandle = null;
					ffprobeHandle = ProcessRunner.RunProcess(ffprobe.FullName, "-v quiet -print_format json -show_format \"" + InputFile.FullName + "\"", stdLine =>
					 {
						 sbOut.AppendLine(stdLine);
					 }, errLine =>
					 {
						 sbErr.AppendLine(errLine);
						 if (abort)
						 {
							 ffprobeHandle.process.CloseMainWindow();
							 ffprobeHandle.process.Kill();
						 }
					 });

					maybeActiveProcess = ffprobeHandle.process;
					int exitCode = ffprobeHandle.WaitForExit();
					maybeActiveProcess = null;
					if (abort)
						return;
					if (exitCode != 0)
						throw new Exception("ffprobe exited with code " + exitCode);

					Ffprobe mediaInfo = Ffprobe.FromJson(sbOut.ToString());
					if (double.TryParse(mediaInfo.Format.Duration, out double durationSeconds))
						sourceVideoDurationSeconds = durationSeconds;
					else
						throw new ApplicationException("Unable to determine video file duration.");
				}

				if (sourceVideoDurationSeconds <= 0)
					throw new ApplicationException("Unable to determine duration of video.");

				onProgress?.Invoke(0, null);

				// Generate thumbnails

				// This method opens ffmpeg once for each thumbnail and is faster with very large files compared to converting with a slow frame rate.
				string outputVideoCodec = losslessThumbnails ? " -c:v png" : " -c:v jpg";

				if (sourceVideoDurationSeconds / captureInterval < minimumCaptures + 1)
					captureInterval = (int)Math.Floor(sourceVideoDurationSeconds / (minimumCaptures + 1));

				if (captureInterval <= 0)
					captureInterval = 1;

				ConcurrentQueue<long> offsets = new ConcurrentQueue<long>();
				for (long i = 0; i < sourceVideoDurationSeconds - captureInterval; i += captureInterval)
					offsets.Enqueue(i);

				long finished = 0;
				double total = offsets.Count;

				Action makeThumb = () =>
				{
					while (!abort && offsets.TryDequeue(out long offset))
					{
						Process maybeActiveFfmpegProcess = null;
						byte[] fileData = null;
						try
						{
							string iframeFilter = captureInterval < 10 ? "" : " -vf select=\"eq(pict_type\\,I)\"";
							string snapshot_args = "-skip_frame nokey -ss " + offset + " -i \"" + InputFile.FullName + "\"" + iframeFilter + outputVideoCodec + " -vframes 1 -f image2pipe -";

							MemoryStream msStd = new MemoryStream();
							StringBuilder sbErr = new StringBuilder();
							ProcessRunnerHandle ffmpegHandle = ProcessRunner.RunProcess_StdBinary_ErrString(ffmpeg.FullName, snapshot_args
								, stdData =>
								{
									msStd.Write(stdData, 0, stdData.Length);
								}
								, errLine =>
								{
									sbErr.AppendLine(errLine);
								});

							maybeActiveFfmpegProcess = ffmpegHandle.process;
							ffmpegHandle.WaitForExit();
							maybeActiveFfmpegProcess = null;

							fileData = msStd.ToArray();
							string ffmpegErrOutput = sbErr.ToString();
						}
						catch (ThreadAbortException) { }
						catch (Exception)
						{
							if (!abort)
								throw;
						}
						finally
						{
							maybeActiveFfmpegProcess?.CloseMainWindow();
							maybeActiveFfmpegProcess?.Kill();
							long f = Interlocked.Increment(ref finished);
							onProgress?.Invoke(f / total, fileData);
						}
					}
				};

				if (abort)
					return;

				SimpleThreadPool pool = new SimpleThreadPool("VideoThumnbailer", 0, maxThreads, 1, true, Logger.Debug);

				for (int i = 0; i < maxThreads; i++)
					pool.Enqueue(makeThumb);

				while (!abort && Interlocked.Read(ref finished) < total)
					Thread.Sleep(1);

				if (abort)
					return;

				onProgress?.Invoke(1, null);
			}
			catch (ThreadAbortException) { abort = true; }
			finally
			{
				maybeActiveProcess?.CloseMainWindow();
				maybeActiveProcess?.Kill();
			}
		}
	}
}
