using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoEncodingAutomation
{
	/// <summary>
	/// An object which can be written to a file and read back with decent error checking.
	/// </summary>
	public class LockFile
	{
		private static UTF8Encoding utf8nobom = new UTF8Encoding(false);
		public string MachineName;
		public string Timestamp;

		public static LockFile Read(Stream stream)
		{
			StreamReader sr = new StreamReader(stream, utf8nobom);
			string json = sr.ReadToEnd();
			try
			{
				LockFile lf = JsonConvert.DeserializeObject<LockFile>(json);
				if (!string.IsNullOrWhiteSpace(lf.MachineName) && !string.IsNullOrWhiteSpace(lf.Timestamp))
					return lf;
			}
			catch { }
			return null;
		}
		public static void Write(Stream stream, LockFile lf)
		{
			string json = JsonConvert.SerializeObject(lf, Formatting.Indented);
			byte[] data = utf8nobom.GetBytes(json);
			stream.Write(data, 0, data.Length);
		}
		public static LockFile CreateNew()
		{
			LockFile lf = new LockFile();
			lf.MachineName = Environment.MachineName;
			lf.Timestamp = DateTime.Now.ToString();
			return lf;
		}
	}
}
