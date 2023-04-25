using BPUtil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NTextCat;
using System.Globalization;
using System.Diagnostics;

namespace VideoEncodingAutomation
{
	/// <summary>
	/// Provides the ability, via ffmpeg and NTextCat, to detect subtitle language when it was not specified in a video file's metadata.
	/// </summary>
	public static class SubtitleLanguageDetector
	{
		/// <summary>
		/// Returns a dictionary of subtitle track number to subtitle file contents, in SRT format.
		/// </summary>
		/// <param name="filePath">Path to a video file.</param>
		/// <param name="subtitleTrackNumbers">Array of subtitle track numbers to extract subtitles for.</param>
		/// <returns></returns>
		public static Dictionary<int, string> GetSubtitleFiles(string filePath, int[] subtitleTrackNumbers)
		{
			using (TempDir tempDir = new TempDir())
			{
				StringBuilder sb = new StringBuilder();
				sb.Append("-i \"" + filePath + "\"");
				foreach (int tn in subtitleTrackNumbers)
				{
					sb.Append(" -map 0:s:" + (tn - 1) + " \"" + SubFilePath(tempDir.FullName, tn) + "\"");
				}
				string args = sb.ToString();
				ProcessRunner.RunProcessAndWait("ffmpeg/ffmpeg.exe", args, out string std, out string err);

				Dictionary<int, string> results = new Dictionary<int, string>();
				foreach (int tn in subtitleTrackNumbers)
				{
					results[tn] = File.ReadAllText(SubFilePath(tempDir.FullName, tn), ByteUtil.Utf8NoBOM);
					File.Delete(SubFilePath(tempDir.FullName, tn));
				}
				return results;
			}
		}
		private static string SubFilePath(string dirPath, int trackNumber)
		{
			return Path.Combine(dirPath, "sub" + trackNumber + ".srt");
		}
		public static string DetectLanguage(string text)
		{
			// Don't forget to deploy a language profile (e.g. Core14.profile.xml) with your application.
			// (take a look at "content" folder inside of NTextCat nupkg and here: https://github.com/ivanakcheurov/ntextcat/tree/master/src/LanguageModels).
			RankedLanguageIdentifierFactory factory = new RankedLanguageIdentifierFactory();
			RankedLanguageIdentifier identifier = factory.Load("NTextCat/Core14.profile.xml"); // can be an absolute or relative path. Beware of 260 chars limitation of the path length in Windows. Linux allows 4096 chars.
			var languages = identifier.Identify(text);
			var mostCertainLanguage = languages.FirstOrDefault();
			if (mostCertainLanguage != null)
			{
				if (iso_language_map_3to2.TryGetValue(mostCertainLanguage.Item1.Iso639_2T, out string twoLetterCode))
					return twoLetterCode;
				else
					return mostCertainLanguage.Item1.Iso639_2T;
			}
			else
				return null;
		}
		private static Dictionary<string, string> iso_language_map_3to2 = MakeLanguageMap();
		private static Dictionary<string, string> MakeLanguageMap()
		{
			Dictionary<string, string> map = new Dictionary<string, string>();
			CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
			foreach (CultureInfo c in cultures)
			{
				if (!string.IsNullOrWhiteSpace(c.ThreeLetterISOLanguageName) && !string.IsNullOrWhiteSpace(c.TwoLetterISOLanguageName))
				{
					map[c.ThreeLetterISOLanguageName] = c.TwoLetterISOLanguageName;
				}
			}
			return map;
		}
	}
}
