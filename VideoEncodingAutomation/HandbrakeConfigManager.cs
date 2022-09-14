using BPUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VideoEncodingAutomation
{
	public class HandbrakeConfigManager
	{
		public readonly string inputFilePath;
		public readonly string outputFilePath;
		/// <summary>
		/// Null if the MediaInfo executable is not found.
		/// </summary>
		public readonly MediaInfo mediaInfo;
		public readonly EncoderConfig encoderConfig;
		public readonly string videoArgs;
		public readonly string audioArgs;
		public readonly string subtitleArgs;
		public readonly string dubArgs;
		public readonly string rangeArgs;

		private static Regex rxReadCrop = new Regex("^(\\d+):(\\d+):(\\d+):(\\d+)$", RegexOptions.Compiled);
		/// <summary>
		/// Constructs a HandbrakeConfigManager. This may take a very long time if the encoderConfig specifies to use "Smart" cropping.
		/// </summary>
		/// <param name="inputFilePath"></param>
		/// <param name="outputFilePath"></param>
		/// <param name="encoderConfig"></param>
		public HandbrakeConfigManager(string inputFilePath, string outputFilePath, EncoderConfig encoderConfig)
		{
			this.inputFilePath = inputFilePath;
			this.outputFilePath = outputFilePath;
			this.encoderConfig = encoderConfig;
			this.mediaInfo = MediaInfo.Load(Program.settings.mediaInfoCLIPath, inputFilePath);

			// Set defaults

			string videoEncoder = encoderConfig.VideoEncoder;

			bool reencodeAudio = true;
			bool hasEngAudio = false;
			int audioKbps = 360;
			string chooseAudioTrack = null;

			subtitleArgs = "--subtitle-lang-list eng"
				+ " --all-subtitles"
				+ " --native-language eng";
			dubArgs = " --native-dub";

			if (encoderConfig.LimitedRange)
			{
				rangeArgs = "--start-at seconds:" + encoderConfig.StartTimeSeconds
						 + " --stop-at seconds:" + encoderConfig.DurationSeconds;
			}

			// Override defaults with better selections based on media info
			if (this.mediaInfo != null)
			{
				// VIDEO
				VideoTrack video = mediaInfo.Video.FirstOrDefault();
				if (video != null)
				{
					if (video.BitDepth == 10)
					{
						if (videoEncoder == "x265" || videoEncoder == "x264")
							videoEncoder += "_10bit";
					}
					else if (video.BitDepth == 12)
					{
						if (videoEncoder == "x265")
							videoEncoder += "_12bit";
						else if (videoEncoder == "x264")
							videoEncoder += "_10bit";
					}
				}

				// AUDIO
				// Require English, no creator commentary.
				// Prefer AAC or AC3, whichever has most channels. If equal, choose AAC.
				AudioTrack[] engAudio = mediaInfo.Audio
					.Where(a => "en".Equals(a.Language, StringComparison.OrdinalIgnoreCase)
							 && (a.Title == null || !a.Title.Contains("comment"))
							 && a.Channels > 0)
					.ToArray();
				hasEngAudio = engAudio.Length > 0;

				AudioTrack withMostChannels = engAudio.OrderByDescending(a => a.Channels).FirstOrDefault();

				AudioTrack aac = engAudio
				   .Where(a => "AAC".Equals(a.Format, StringComparison.OrdinalIgnoreCase)
							|| "AAC LC".Equals(a.Format, StringComparison.OrdinalIgnoreCase)
							|| "A_AAC-2".Equals(a.Format, StringComparison.OrdinalIgnoreCase))
				   .OrderByDescending(a => a.Channels)
				   .FirstOrDefault();
				AudioTrack ac3 = engAudio
					.Where(a => "AC-3".Equals(a.Format, StringComparison.OrdinalIgnoreCase)
							 || "E-AC-3".Equals(a.Format, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(a => a.Channels)
					.FirstOrDefault();
				AudioTrack best = null;
				if (aac != null && ac3 != null)
				{
					best = ac3.Channels > aac.Channels ? ac3 : aac;
				}
				else if (aac != null)
				{
					best = aac;
				}
				else if (ac3 != null)
				{
					best = ac3;
				}
				if (best != null && best.Channels <= 5 && withMostChannels.Channels > best.Channels)
					best = null; // Invalidate "best" track if it is less than 5.1 surround and there is a different track with more audio channels.
				if (best != null)
				{
					reencodeAudio = false;
					chooseAudioTrack = best.GetOrderArgument();
				}
				else
				{
					// Handbrake defaults, 7.1 source:                    Output Channels   Subjective notes
					//
					//	AAC 7.1 (5_2_lfe):    bitrate 255 or less         7                 sounds bad. 0-255 yields same as default.
					//                                                                      256-319 sounds middle. 
					//                                                                      320-383 sounds good. 
					//                                                                      384-400+ is bigger, not better.
					//                                                                      This mixdown is apparently not supported for AAC, based on 
					//                                                                      Handbrake source code, so it is probably falling back to a  
					//                                                                      lower mixdown.
					//
					//  AAC 7point1:          ^^                          7                 same as above. No change to audio stream.
					//
					//  AAC 6point1:          ^^                          7                 same as above. No change to audio stream.
					//
					//  AAC 5point1:                                      6                 sounds bad (unknown bitrate). (limited testing)
					//                                                                      240-250 sounds good. 
					//                                                                      256-300 is bigger, not better. 
					//                                                                      320 is bigger, not better.
					//
					//	AAC Stereo:           bitrate 128                 2                 sounds fine. 80 sounds a bit worse. 120 is about the same.
					//
					//  If no mixdown specified, we get Stereo @ 128 Kbps as above.

					AudioTrack first = engAudio.FirstOrDefault();
					if (first == null && mediaInfo.Audio.Length == 1)
						first = mediaInfo.Audio.First();
					chooseAudioTrack = first?.GetOrderArgument();
					int channels = first == null ? 7 : first.Channels;
					if (channels >= 7)
						audioKbps = 360; // A range of 320-383 was tested to produce the same output at 7ch.
					else if (channels == 6)
						audioKbps = 250; // This was tested to sound fine.
					else if (channels == 5)
						audioKbps = 224; // Wild guess
					else if (channels == 4)
						audioKbps = 192; // Wild guess
					else
						audioKbps = 128;
				}
				// SUBTITLES
				TextTrack[] engSubs = mediaInfo.Text.Where(t => "en".Equals(t.Language, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (engSubs.Length == 0 && mediaInfo.Text.Length > 0)
				{
					if (mediaInfo.Text.All(tt => string.IsNullOrWhiteSpace(tt.Language)) // All text tracks are missing language data.
						|| mediaInfo.Text.Any(tt => "PGS".Equals(tt.Format, StringComparison.OrdinalIgnoreCase))) // At least one text track is PGS format (bitmap subtitles).
					{
						subtitleArgs = "--all-subtitles";
						dubArgs = "";
					}
				}
				else
				{
					TextTrack forced = engSubs.FirstOrDefault(t => IsYes(t.Forced));
					TextTrack defaultSub = engSubs.FirstOrDefault(t => IsYes(t.Default));
					// If there is no default subtitle track, we'll make the default be the first forced subs track.
					// (forced subs are those normally shown even when captions are not enabled)
					if (defaultSub == null)
					{
						for (int i = 0; i < engSubs.Length; i++)
						{
							if (IsYes(engSubs[i].Forced))
							{
								subtitleArgs = "-s " + string.Join(",", engSubs.Select(t => t.GetOrderArgument()))
											+ " --subtitle-default=" + (i + 1);
								dubArgs = "";
								break;
							}
						}
					}
				}
			}
			if (chooseAudioTrack != null)
			{
				audioArgs = "--audio " + chooseAudioTrack;
				if (!reencodeAudio)
				{
					audioArgs += " --aencoder copy";
				}
			}
			else if (hasEngAudio)
			{
				// We didn't choose a specific track.
				audioArgs = "--audio-lang-list eng,und"
				   + " --first-audio";
			}
			else
			{
				// There's no english.  Just copy the first audio stream.
				audioArgs = "--first-audio";
				// Experimentation shows that "--native-dub" will prevent foreign audio from being included.
				dubArgs = "";
			}
			if (reencodeAudio)
			{
				audioArgs += " --aencoder ca_aac"
						+ " --mixdown 5_2_lfe"
						+ " --ab " + audioKbps;
				//+ " --audio-fallback av_aac"
			}
			else
			{
				audioArgs += " --aencoder copy";
			}

			string cropArg = "";
			if (!string.IsNullOrWhiteSpace(encoderConfig.HandbrakeCrop))
			{
				if (encoderConfig.HandbrakeCrop.Equals("Smart", StringComparison.OrdinalIgnoreCase))
					cropArg = GenerateSmartCropArg(inputFilePath);
				else
				{
					Match m = rxReadCrop.Match(encoderConfig.HandbrakeCrop);
					if (m.Success)
						cropArg = " --crop " + encoderConfig.HandbrakeCrop;
				}
			}

			videoArgs = "-e " + videoEncoder
					 + " --encoder-preset " + encoderConfig.VideoEncoderPreset
					 + " -q " + encoderConfig.Quality
					 + cropArg
					 + " --modulus 2";
		}

		/// <summary>
		/// Runs CropCalculator on the input video to determine the optimal crop size. This can easily take a minute or longer. 
		/// </summary>
		/// <param name="inputFilePath"></param>
		/// <returns></returns>
		private string GenerateSmartCropArg(string inputFilePath)
		{
			Cropping.CropCalculator cc = new Cropping.CropCalculator(inputFilePath
				, (progress, imageFile) =>
				{
					imageFile?.Dispose();
				}
				, false);
			Logger.Info("HandbrakeConfigManager: Computing crop dimensions…");
			Cropping.CropRectangle rect = cc.CalculateCrop();
			if (rect == null)
				return "";
			else
				return " --crop " + rect.ToHandbrakeString();
		}

		public string GetHandbrakeArgs()
		{
			if (encoderConfig.Encoder != "handbrake")
				throw new Exception("GetHandbrakeArgs() does not support encoder \"" + encoderConfig.Encoder + "\"");

			return "-i \"" + inputFilePath + "\""
				+ " -o \"" + outputFilePath + "\""
				+ " " + videoArgs
				+ " " + audioArgs
				+ " " + subtitleArgs
				+ dubArgs
				+ (outputFilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? " -O" : "")
				+ (!string.IsNullOrWhiteSpace(rangeArgs) ? " " + rangeArgs : "");
		}
		//private string ProcessHandbrakeArgs(string str)
		//{
		//	List<string> args = new List<string>();
		//	string[] lines = str.Split('\r', '\n');
		//	for (int i = 0; i < lines.Length; i++)
		//	{
		//		string line = lines[i].Trim();
		//		if (line != "" && !line.StartsWith("#"))
		//		{
		//			args.Add(line);
		//		}
		//	}
		//	return string.Join(" ", args);
		//}
		private static bool IsYes(string yesno)
		{
			return "yes".Equals(yesno, StringComparison.OrdinalIgnoreCase);
		}
	}
}
