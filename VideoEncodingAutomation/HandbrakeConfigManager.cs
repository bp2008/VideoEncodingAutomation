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
			if (videoEncoder == "av1")
				videoEncoder = "svt_av1";

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
						if (videoEncoder == "x265" || videoEncoder == "x264" || videoEncoder == "svt_av1")
							videoEncoder += "_10bit";
					}
					else if (video.BitDepth == 12)
					{
						if (videoEncoder == "x265")
							videoEncoder += "_12bit";
						else if (videoEncoder == "x264")
							videoEncoder += "_10bit";
						else if (videoEncoder == "svt_av1")
							videoEncoder += "_10bit";
					}
				}

				// AUDIO
				HashSet<int> chosenAudioTrackNumbers = new HashSet<int>();
				AudioTrack[] engAudio = mediaInfo.Audio
					.Where(a => "en".IEquals(a.Language))
					.OrderBy(a => a.HandbrakeStreamNumber)
					.ToArray();
				AudioTrack[] engAudioNoCommentary = engAudio
					.Where(a => !IsAudioCommentary(a))
					.OrderBy(a => a.HandbrakeStreamNumber)
					.ToArray();
				AudioTrack bestEng = engAudioNoCommentary
					.OrderByDescending(a => a.Channels)
					.ThenBy(a => a.HandbrakeStreamNumber)
					.FirstOrDefault();

				if (bestEng != null)
					chosenAudioTrackNumbers.Add(bestEng.HandbrakeStreamNumber);

				if (chosenAudioTrackNumbers.Count == 0 || encoderConfig.AudioTrackSelection.AllEnglishNoCommentary)
					chosenAudioTrackNumbers.AddRange(engAudioNoCommentary.Select(a => a.HandbrakeStreamNumber));

				if (chosenAudioTrackNumbers.Count == 0 || encoderConfig.AudioTrackSelection.AllEnglish)
					chosenAudioTrackNumbers.AddRange(engAudio.Select(a => a.HandbrakeStreamNumber));

				if (chosenAudioTrackNumbers.Count == 0 || encoderConfig.AudioTrackSelection.AllTracksNoCommentary)
					chosenAudioTrackNumbers.AddRange(mediaInfo.Audio.Where(a => !IsAudioCommentary(a)).Select(a => a.HandbrakeStreamNumber));

				if (chosenAudioTrackNumbers.Count == 0 || encoderConfig.AudioTrackSelection.AllTracks)
					chosenAudioTrackNumbers.AddRange(mediaInfo.Audio.Select(a => a.HandbrakeStreamNumber));

				audioArgs = GetAudioArgs(mediaInfo.Audio.Where(a => chosenAudioTrackNumbers.Contains(a.HandbrakeStreamNumber)).OrderByDescending(a => a.HandbrakeStreamNumber).ToArray());

				// SUBTITLES
				TextTrack[] engSubs = mediaInfo.Text.Where(t => "en".IEquals(t.Language)).ToArray();
				if (engSubs.Length == 0 || encoderConfig.SubtitleTrackSelection.AllTracks)
				{
					subtitleArgs = "--all-subtitles";
					// Additional conditions removed March 2023; if no English, take all subs in case languages were mislabeled.
					//if (mediaInfo.Text.All(tt => string.IsNullOrWhiteSpace(tt.Language)) // All text tracks are missing language data.
					//	|| mediaInfo.Text.Any(tt => "PGS".Equals(tt.Format, StringComparison.OrdinalIgnoreCase))) // At least one text track is PGS format (bitmap subtitles) which we can't detect language on if it wasn't tagged properly.
					//{ }
				}
				else
				{
					subtitleArgs = "--subtitle-lang-list eng --all-subtitles";
					//+ " --native-language eng";
					TextTrack defaultSub = engSubs.FirstOrDefault(t => IsYes(t.Default));
					// If there is no default subtitle track, we'll make the default be the first forced subs track.
					// (forced subs are those intended to be shown even when captions are not enabled)
					if (defaultSub == null)
					{
						for (int i = 0; i < engSubs.Length; i++)
						{
							if (IsYes(engSubs[i].Forced))
							{
								// Include english subs via explicit list, and specify which one is default.
								subtitleArgs = "-s " + string.Join(",", engSubs.Select(t => t.HandbrakeStreamNumber))
											+ " --subtitle-default=" + (i + 1);
								break;
							}
						}
					}
				}
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

		private bool IsAudioCommentary(AudioTrack a)
		{
			return a.Title != null && (a.Title.IContains("comment") || a.Title.IContains("director") || a.Title.IContains("actor"));
		}

		/// <summary>
		/// Returns an audio arguments string, or "" if no tracks are provided.
		/// </summary>
		/// <param name="tracks"></param>
		/// <returns></returns>
		private string GetAudioArgs(AudioTrack[] tracks)
		{
			if (tracks.Length == 0)
				return "";
			List<int> trackNumbers = new List<int>();
			List<string> aencoders = new List<string>();
			List<int> abitrates = new List<int>();
			foreach (AudioTrack track in tracks)
			{
				int desiredBitrate;
				if (track.Channels >= 7)
					desiredBitrate = 320;
				else if (track.Channels == 6)
					desiredBitrate = 256;
				else if (track.Channels == 5)
					desiredBitrate = 224;
				else if (track.Channels == 4)
					desiredBitrate = 192;
				else
					desiredBitrate = 128; // 1-3 channels

				string f = track.Format;
				bool isBasicCodec = f == null ? false : (f.IEquals("AAC") || f.IEquals("AAC LC") || f.IEquals("A_AAC-2") || f.IEquals("AC-3") || f.IEquals("E-AC-3") || f.IContains("opus"));
				int bitRateExisting = int.TryParse(track.BitRate, out int bitsPerSecond) && bitsPerSecond > 6000 ? bitsPerSecond / 1000 : 0;
				if (isBasicCodec && bitRateExisting > 0 && bitRateExisting <= desiredBitrate * 1.2)
					aencoders.Add("copy"); // Stream is a good or widely-supported codec, bit rate is specified and is small enough that we wouldn't save much by re-encoding. Copy the stream.
				else
					aencoders.Add("opus"); // Re-encode to opus

				if (bitRateExisting > 0 && bitRateExisting < desiredBitrate)
					desiredBitrate = bitRateExisting; // Absolutely no need to encode to a bitrate higher than the source.  Opus is one of the best audio codecs.
				abitrates.Add(desiredBitrate);
				trackNumbers.Add(track.HandbrakeStreamNumber);
			}
			return "--audio " + string.Join(",", trackNumbers)
				+ " --aencoder " + string.Join(",", aencoders)
				+ " --ab " + string.Join(",", abitrates)
				+ " --audio-fallback opus";
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
				+ (!string.IsNullOrWhiteSpace(audioArgs) ? " " + audioArgs : "")
				+ (!string.IsNullOrWhiteSpace(subtitleArgs) ? " " + subtitleArgs : "")
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

// Require English, no creator commentary.
// Prefer Opus, AAC or AC3, whichever has most channels. If equal, choose Opus.
// Try to find the best stream to copy.
//AudioTrack[] engEligibleForBest = engAudio
//	.Where(a => a.Format != null)
//	.ToArray();
//AudioTrack opus = engEligibleForBest
//	.Where(a => a.Format.IContains("opus"))
//	.Where(a => int.TryParse(a.BitRate, out int bitRate) && bitRate <= 260000)
//	.OrderByDescending(a => a.Channels)
//	.ThenBy(a => a.HandbrakeStreamNumber)
//	.FirstOrDefault();
//AudioTrack aac = engEligibleForBest
//	.Where(a => a.Format.IEquals("AAC")
//			|| a.Format.IEquals("AAC LC")
//			|| a.Format.IEquals("A_AAC-2"))
//	.Where(a => int.TryParse(a.BitRate, out int bitRate) && bitRate <= 380000)
//	.OrderByDescending(a => a.Channels)
//	.ThenBy(a => a.HandbrakeStreamNumber)
//	.FirstOrDefault();
//AudioTrack ac3 = engEligibleForBest
//	.Where(a => a.Format.IEquals("AC-3")
//				|| a.Format.IEquals("E-AC-3"))
//	.Where(a => int.TryParse(a.BitRate, out int bitRate) && bitRate <= 420000)
//	.OrderByDescending(a => a.Channels)
//	.ThenBy(a => a.HandbrakeStreamNumber)
//	.FirstOrDefault();
//AudioTrack best = opus;
//if (best == null || (aac != null && best.Channels < aac.Channels))
//	best = aac;
//if (best == null || (ac3 != null && best.Channels < ac3.Channels))
//	best = ac3;
//if (best != null && best.Channels <= 5 && engWithMostChannels.Channels > best.Channels)
//	best = null; // Invalidate "best" track if it is less than 5.1 surround and there is a different track with more audio channels.
//if (best != null)
//{
//	audioArgs = "--audio " + best.HandbrakeStreamNumber;
//	audioArgs += " --aencoder copy";
//}
//else
//{
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

// The following logic was removed March 2023 but kept in comments because it contains some test results in comments.
//int audioKbps = 360;
//AudioTrack first = engAudio.FirstOrDefault();
//if (first == null && mediaInfo.Audio.Length == 1)
//	first = mediaInfo.Audio.First();
//int channels = first == null ? 7 : first.Channels;
//if (channels >= 7)
//	audioKbps = 360; // A range of 320-383 was tested to produce the same output at 7ch.
//else if (channels == 6)
//	audioKbps = 250; // This was tested to sound fine.
//else if (channels == 5)
//	audioKbps = 224; // Wild guess
//else if (channels == 4)
//	audioKbps = 192; // Wild guess
//else
//	audioKbps = 128;
//if (engAudio.Length > 0)
//{
//	// We didn't choose a specific track.
//	audioArgs = "--audio-lang-list eng,und"
//	   + " --first-audio";
//}
//else
//{
//	// There's no english.  Just copy the first audio stream.
//	audioArgs = "--first-audio";
//	// Experimentation shows that "--native-dub" will prevent foreign audio from being included.
//	dubArgs = "";
//}
//audioArgs = " --aencoder opus"
//		 + " --mixdown 5_2_lfe"
//		 + " --ab " + audioKbps;
//}

//audioArgs = "--audio none";