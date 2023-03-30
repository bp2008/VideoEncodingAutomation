﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoEncodingAutomation
{
	public class EncoderConfig
	{
		/// <summary>
		/// "handbrake", "ffmpeg"
		/// </summary>
		public string Encoder = "handbrake";
		/// <summary>
		/// "x264", "x265", "av1".
		/// Higher bit-depth versions ["x264_10bit", "x265_10bit", "x265_12bit"] are handled automatically if MediaInfo CLI is configured.
		/// </summary>
		public string VideoEncoder = "x265";
		/// <summary>
		/// "ultrafast", "medium", etc
		/// </summary>
		public string VideoEncoderPreset = "medium";
		/// <summary>
		/// 0-51, where 0 is best.  "Default" for x265 is 28.  Common wisdom for x264 says 18 is visually lossless.  I prefer 23 for a balance.
		/// </summary>
		public int Quality = 23;
		/// <summary>
		/// Crop argument used for Handbrake: Picture cropping in pixels. &lt;top:bottom:left:right&gt;. If "Smart", then a preprocess is used to compute the best crop size at the cost of slightly extended processing time. Handbrake has autocropping capability but it isn't used here because it crops a bit too aggressively.
		/// </summary>
		public string HandbrakeCrop = "0:0:0:0";
		/// <summary>
		/// If true, all audio tracks are captured. If false, an algorithm tries to find the best English language track. Regardless, an algorithm decides whether or not to recompress.
		/// </summary>
		public bool AllAudioTracks = false;
		/// <summary>
		/// If true, all text tracks are captured. If false, only English tracks are captured.
		/// </summary>
		public bool AllTextTracks = false;

		public bool LimitedRange = false;
		public int StartTimeSeconds = 0;
		public int DurationSeconds = 0;
		public bool KeepInputForDebuggingAfterward = false;

		/// <summary>
		/// Validates that all the inputs make sense, returning true if successful or an error message if validity checking failed.
		/// </summary>
		/// <returns></returns>
		public string CheckValidity()
		{
			if (LimitedRange)
			{
				if (StartTimeSeconds < 0)
					return "StartTimeSeconds must be > -1";
				if (DurationSeconds < 1)
					return "DurationSeconds must be > 0";
			}

			if (Encoder == "handbrake")
			{
				if (VideoEncoder == "x265" || VideoEncoder == "x264")
				{
					HashSet<string> supportedPresets = new HashSet<string>(new string[] {
						"ultrafast",
						"superfast",
						"veryfast",
						"faster",
						"fast",
						"medium",
						"slow",
						"slower",
						"veryslow",
						"placebo"
					});
					if (!supportedPresets.Contains(VideoEncoderPreset))
						return "Unsupported VideoEncoderPreset";
					if (Quality < 0 || Quality > 51)
						return "Unsupported Quality (range must be [0-51])";
					return null;
				}
				else if (VideoEncoder == "av1")
				{
					if (!int.TryParse(VideoEncoderPreset, out int preset) || preset < 1 || preset > 13)
						return "Unsupported VideoEncoderPreset. Accepted AV1 presets are integers from 1 to 13.";
					if (Quality < 0 || Quality > 63)
						return "Unsupported Quality (range must be [0-63])";
					return null;
				}
				else
					return "VideoEncoder must be x265 or x264 or av1";
			}
			else
				return "Encoder must be handbrake";
		}
	}
}
