using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoEncodingAutomation;

namespace MediaInfoTest
{
	class Program
	{
		static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("No filename was provided as an argument.");
				return 1;
			}
			MediaInfo mediaInfo = MediaInfo.Load("MediaInfo.exe", args[0]);
			Console.WriteLine(mediaInfo.reprocessedJson);
			if (Debugger.IsAttached)
			{
				Console.WriteLine("Press Enter to exit.");
				Console.ReadLine();
			}
			return 0;
		}
	}
}
