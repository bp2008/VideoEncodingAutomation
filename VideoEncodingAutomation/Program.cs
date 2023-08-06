using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BPUtil;
using Newtonsoft.Json;

namespace VideoEncodingAutomation
{
	class Program
	{
#if DEBUG
		public const bool isDebug = true;
#else
		public const bool isDebug = false;
#endif
		public static Settings settings;
		static void Main(string[] args)
		{
			if (!isDebug && System.Diagnostics.Debugger.IsAttached)
			{
				Console.WriteLine("Do not run this program in Visual Studio in Release mode, as that will dirty up the Release directory!");
				Console.ReadLine();
				return;
			}
			Globals.Initialize(System.Reflection.Assembly.GetExecutingAssembly().Location);

			BPUtil.Logger.logType = BPUtil.LoggingMode.Console | BPUtil.LoggingMode.File;

			Console.WriteLine("VideoEncodingAutomation");

			EndPreExistingHandBrakeInstances();

			settings = new Settings();
			settings.Load(Globals.WritableDirectoryBase + "Settings.cfg");
			if (isDebug || !System.Diagnostics.Debugger.IsAttached)
				settings.Save(Globals.WritableDirectoryBase + "Settings.cfg");

			File.WriteAllText(Globals.WritableDirectoryBase + "encoder-default.txt", JsonConvert.SerializeObject(new EncoderConfig(), Formatting.Indented), Encoding.UTF8);
			WebServer ws = new WebServer();
			ws.SetBindings(settings.webPort);

			Console.WriteLine("Web server listening on port " + settings.webPort + ".  Type \"exit\" to shut down.");
			while (Console.ReadLine().ToLower() != "exit")
			{
				Console.WriteLine("Type \"exit\" to shut down.");
			}
			Console.WriteLine("Shutting down...");

			ws.Stop();
		}

		private static void EndPreExistingHandBrakeInstances()
		{
			try
			{
				if (Process.GetProcessesByName("VideoEncodingAutomation").Length > 1)
					return;
				Process[] procs = Process.GetProcessesByName("HandBrakeCLI");
				if (procs.Length > 0)
				{
					Logger.Info("Killing " + procs.Length + " pre-existing HandBrakeCLI instances.");
					foreach (Process process in procs)
					{
						try
						{
							process.Kill();
							Thread.Sleep(100);
						}
						catch (Exception ex)
						{
							Logger.Debug(ex);
						}
					}
					Thread.Sleep(1000);
					procs = Process.GetProcessesByName("HandBrakeCLI");
					if (procs.Length > 0)
					{
						Logger.Debug("Unable to kill " + procs.Length + " pre-existing HandBrakeCLI instances. Exiting now.");
						return;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Debug(ex);
			}
		}
	}
}
