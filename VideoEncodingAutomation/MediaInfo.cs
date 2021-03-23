using BPUtil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VideoEncodingAutomation
{
	/// <summary>
	/// Based on output from MediaInfo 19.09.
	/// </summary>
	public class MediaInfo
	{
		[JsonProperty("@ref")]
		public string filePath;

		[JsonProperty("track")]
		public Track[] tracks;

		public GeneralTrack[] General { get { return tracks.OfType<GeneralTrack>().ToArray(); } }
		public VideoTrack[] Video { get { return tracks.OfType<VideoTrack>().ToArray(); } }
		public AudioTrack[] Audio { get { return tracks.OfType<AudioTrack>().ToArray(); } }
		public TextTrack[] Text { get { return tracks.OfType<TextTrack>().ToArray(); } }
		public MenuTrack[] Menu { get { return tracks.OfType<MenuTrack>().ToArray(); } }

		[JsonIgnore]
		public string json;

		public static MediaInfo Load(string mediaInfoCLIPath, string FilePath)
		{
			if (IsAvailable(mediaInfoCLIPath))
			{
				ProcessRunner.RunProcessAndWait(mediaInfoCLIPath, "--Output=JSON \"" + FilePath + "\"", out string std, out string err);
				if (!string.IsNullOrWhiteSpace(std))
				{
					MediaInfoWrapper miw = JsonConvert.DeserializeObject<MediaInfoWrapper>(std, new TrackConverter());
					miw.media.json = JValue.Parse(std).ToString(Formatting.Indented);
					return miw.media;
				}
			}
			return null;
		}

		public static bool IsAvailable(string mediaInfoCLIPath)
		{
			if (string.IsNullOrWhiteSpace(mediaInfoCLIPath))
				return false;
			return File.Exists(mediaInfoCLIPath);
		}
	}
	internal class MediaInfoWrapper
	{
#pragma warning disable CS0649
		public MediaInfo media;
#pragma warning restore CS0649
	}
	internal class TrackConverter : JsonCreationConverter<Track>
	{
		protected override Track Create(Type objectType, JObject jObject)
		{
			switch ((string)jObject["@type"])
			{
				case "General":
					return new GeneralTrack();
				case "Video":
					return new VideoTrack();
				case "Audio":
					return new AudioTrack();
				case "Text":
					return new TextTrack();
				case "Menu":
					return new MenuTrack();
				default:
					return new Track();
			}
		}

		private bool FieldExists(string fieldName, JObject jObject)
		{
			return jObject[fieldName] != null;
		}
	}
	internal abstract class JsonCreationConverter<T> : JsonConverter
	{
		/// <summary>
		/// Create an instance of objectType, based properties in the JSON object
		/// </summary>
		/// <param name="objectType">type of object expected</param>
		/// <param name="jObject">contents of JSON object that will be deserialized</param>
		/// <returns></returns>
		protected abstract T Create(Type objectType, JObject jObject);

		public override bool CanConvert(Type objectType)
		{
			return typeof(T).IsAssignableFrom(objectType);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			// Load JObject from stream
			JObject jObject = JObject.Load(reader);
			// Create target object based on JObject
			T target = Create(objectType, jObject);
			// Populate the object properties
			using (JsonReader jObjectReader = CopyReaderForObject(reader, jObject))
			{
				serializer.Populate(jObjectReader, target);
			}
			return target;
		}

		public override bool CanWrite
		{
			get { return false; }
		}
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Creates a new reader for the specified jObject by copying the settings from an existing reader.
		/// </summary>
		/// <param name="reader">The reader whose settings should be copied.</param>
		/// <param name="jToken">The jToken to create a new reader for.</param>
		/// <returns>The new disposable reader.</returns>
		public static JsonReader CopyReaderForObject(JsonReader reader, JToken jToken)
		{
			JsonReader jTokenReader = jToken.CreateReader();
			jTokenReader.Culture = reader.Culture;
			jTokenReader.DateFormatString = reader.DateFormatString;
			jTokenReader.DateParseHandling = reader.DateParseHandling;
			jTokenReader.DateTimeZoneHandling = reader.DateTimeZoneHandling;
			jTokenReader.FloatParseHandling = reader.FloatParseHandling;
			jTokenReader.MaxDepth = reader.MaxDepth;
			jTokenReader.SupportMultipleContent = reader.SupportMultipleContent;
			return jTokenReader;
		}
	}
	public class Track
	{
		[JsonProperty("@type")]
		public string type;
	}
	public class GeneralTrack : Track
	{
		public string UniqueID;
		public int VideoCount;
		public int AudioCount;
		public int TextCount;
		public int MenuCount;
		public string FileExtension;
		public string Format;
		public string Format_Version;
		public long FileSize;
		public decimal Duration;
		public string OverallBitRate_Mode;
		public long OverallBitRate;
		public decimal FrameRate;
		public long FrameCount;
		public long StreamSize;
		public string IsStreamable;
		public string Title;
		public string Movie;
		public string Encoded_Date;
		public string File_Created_Date;
		public string File_Created_Date_Local;
		public string File_Modified_Date;
		public string File_Modified_Date_Local;
		public string Encoded_Application;
		public string Encoded_Library;
	}
	public class VideoTrack : Track
	{
		public string StreamOrder;
		public string ID;
		public string UniqueID;
		public string Format;
		public string Format_Profile;
		public string Format_Level;
		public string Format_Tier;
		public string HDR_Format;
		public string HDR_Format_Compatibility;
		public string CodecID;
		public decimal Duration;
		public string BitRate;
		public long Width;
		public long Height;
		public long Sampled_Width;
		public long Sampled_Height;
		public decimal PixelAspectRatio;
		public decimal DisplayAspectRatio;
		public string FrameRate_Mode;
		public decimal FrameRate;
		public long FrameCount;
		public string ColorSpace;
		public string ChromaSubsampling;
		public string ChromaSubsampling_Position;
		public int BitDepth;
		public decimal Delay;
		public long StreamSize;
		public string Encoded_Library;
		public string Encoded_Library_Name;
		public string Encoded_Library_Version;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string Default;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string Forced;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string colour_description_present;
		public string colour_description_present_Source;
		public string colour_range;
		public string colour_range_Source;
		public string colour_primaries;
		public string colour_primaries_Source;
		public string transfer_characteristics;
		public string transfer_characteristics_Source;
		public string matrix_coefficients;
		public string matrix_coefficients_Source;
		public string MasteringDisplay_ColorPrimaries;
		public string MasteringDisplay_ColorPrimaries_Source;
		public string MasteringDisplay_Luminance;
		public string MasteringDisplay_Luminance_Source;
	}
	public class AudioTrack : Track
	{
		[JsonProperty("@typeorder")]
		public string typeorder;
		public string StreamOrder;
		public string ID;
		public string UniqueID;
		public string Format;
		public string Format_Commercial_IfAny;
		public string Format_AdditionalFeatures;
		public string Format_Settings_Endianness;
		public string CodecID;
		public decimal Duration;
		public string BitRate;
		public int Channels;
		public string ChannelPositions;
		public string ChannelLayout;
		public int SamplesPerFrame;
		public int SamplingRate;
		public long SamplingCount;
		public decimal FrameRate;
		public long FrameCount;
		public string Compression_Mode;
		public decimal Delay;
		public string Delay_Source;
		public long StreamSize;
		public decimal StreamSize_Proportion;
		public string Title;
		/// <summary>
		/// "en"
		/// </summary>
		public string Language;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string Default;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string Forced;
		public object extra;
		public string GetOrderArgument()
		{
			if (!string.IsNullOrWhiteSpace(typeorder))
				return typeorder;
			if (!string.IsNullOrWhiteSpace(StreamOrder))
				return StreamOrder;
			throw new Exception("No typeorder or StreamOrder field value in this AudioTrack");
		}
	}
	public class TextTrack : Track
	{
		[JsonProperty("@typeorder")]
		public string typeorder;
		public string ID;
		public string UniqueID;
		public string Format;
		public string CodecID;
		public decimal Duration;
		public string BitRate;
		public decimal FrameRate;
		public long FrameCount;
		public long ElementCount;
		public long StreamSize;
		public string Title;
		/// <summary>
		/// "en"
		/// </summary>
		public string Language;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string Default;
		/// <summary>
		/// "Yes" / "No"
		/// </summary>
		public string Forced;
		public string GetOrderArgument()
		{
			return typeorder;
		}
	}
	public class MenuTrack : Track
	{
		public object extra;
	}
}
