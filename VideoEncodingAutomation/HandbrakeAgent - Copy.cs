//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using BPUtil;

//namespace VideoEncodingAutomation
//{
//	public class HandbrakeAgent
//	{
//		private ConcurrentQueue<Tuple<string, string, string>> taskQueue = new ConcurrentQueue<Tuple<string, string, string>>();

//		private volatile bool isActive = false;
//		private volatile bool started = false;
//		private Thread thrSchedulingThread;

//		public bool Active
//		{
//			get
//			{
//				return isActive;
//			}
//		}

//		public HandbrakeAgent()
//		{
//			thrSchedulingThread = new Thread(schedulingLoop);
//			thrSchedulingThread.Name = "HandbrakeAgent";
//		}
//		public void Start()
//		{
//			lock (this)
//			{
//				if (started)
//					throw new Exception("This instance has already been used!");
//				started = true;
//			}
//			isActive = true;
//			thrSchedulingThread.Start();
//		}

//		public void Shutdown()
//		{
//			try
//			{
//				thrSchedulingThread.Abort();
//			}
//			catch (Exception) { }
//		}
//		private void AddItemToSchedule(string inputFile, string outputFile, string destinationFile)
//		{
//			taskQueue.Enqueue(new Tuple<string, string, string>(inputFile, outputFile, destinationFile));
//		}
//		private void schedulingLoop()
//		{
//			try
//			{
//				while (true)
//				{
//					Tuple<string, string, string> item;
//					if (taskQueue.TryDequeue(out item))
//					{
//						// A task is available. Run it.
//						FileInfo fiIn = new FileInfo(item.Item1);
//						if (!fiIn.Exists)
//							continue;

//						FileInfo fiOut = new FileInfo(item.Item2);
//						if (!fiOut.Directory.Exists)
//							Directory.CreateDirectory(fiOut.Directory.FullName);
//						if (fiOut.Exists)
//							fiOut.Delete();

//						FileInfo fiFinalDestination = new FileInfo(item.Item3);
//						if (!fiFinalDestination.Directory.Exists)
//							Directory.CreateDirectory(fiFinalDestination.Directory.FullName);
//						if (fiFinalDestination.Exists)
//						{
//							Logger.Debug("HandbrakeAgent: Destination file already exists; please deal with this! " + fiFinalDestination.FullName);
//							continue;
//						}

//						Logger.Debug("HandbrakeAgent: Beginning to process " + fiIn.Name);
//						// "C:\Program Files\Handbrake\HandBrakeCLI.exe" -i "C:\DTVRip\Encoding\Game of Thrones - 20150607 - The Dance of Dragons - 2015-06-11 07-58-03 PM.ts" -o "out.mp4" -e x264 -q 20 -E copy -O
//						ProcessStartInfo psi = new ProcessStartInfo(@"C:\Program Files\Handbrake\HandBrakeCLI.exe", "-i \"" + fiIn.FullName + "\" -o \"" + fiOut.FullName + "\" -e x264 -q " + Program.settings.quality + " -E copy -O -w 1920 -l 1080 --crop 0:0:0:0 --modulus 2");
//						psi.CreateNoWindow = true;
//						//psi.UseShellExecute = false;
//						//psi.RedirectStandardOutput = true;
//						//psi.RedirectStandardError = true;
//						Process process = new Process();
//						process.StartInfo = psi;
//						process.Start();

//						//object consoleLock = new object();
//						//bool readStdOutFinished = false;
//						//bool readStdErrFinished = false;
//						//Thread thrReadStdOut = new Thread(() =>
//						//{
//						//	do
//						//	{
//						//		string s = process.StandardOutput.ReadLine();
//						//		if (s == null)
//						//			break;
//						//		lock (consoleLock)
//						//			Console.WriteLine(s);
//						//	}
//						//	while (true);
//						//	readStdOutFinished = true;
//						//});
//						//Thread thrReadStdErr = new Thread(() =>
//						//{
//						//	do
//						//	{
//						//		string s = process.StandardError.ReadLine();
//						//		if (s == null)
//						//			break;
//						//		lock (consoleLock)
//						//			Console.Error.WriteLine(s);
//						//	}
//						//	while (true);
//						//	readStdErrFinished = true;
//						//});
//						//thrReadStdOut.Name = "StdOutReader";
//						//thrReadStdErr.Name = "StdErrReader";
//						//thrReadStdOut.Start();
//						//thrReadStdErr.Start();

//						process.WaitForExit();

//						//Logger.Debug("HandbrakeAgent: Waiting for output streams to finish.");

//						//while (!readStdOutFinished && !readStdErrFinished)
//						//{
//						//	Thread.Sleep(50);
//						//}


//						fiOut.Refresh();
//						if (!fiOut.Exists)
//						{
//							Logger.Debug("HandbrakeAgent: Expected output file " + fiOut.FullName + " does not exist!  Did handbrake run correctly?");
//						}
//						else
//						{
//							Logger.Debug("HandbrakeAgent: Moving " + fiOut.FullName + " to " + fiFinalDestination.FullName);
//							fiOut.MoveTo(fiFinalDestination.FullName);

//							Logger.Debug("HandbrakeAgent: Deleting " + fiIn.FullName);

//							fiIn.Delete();

//							Logger.Debug("HandbrakeAgent: Done with " + fiIn.Name);
//						}
//					}
//					else
//					{
//						// Search for items
//						DirectoryInfo diInput = new DirectoryInfo(Program.settings.inputDir);
//						foreach (FileInfo fi in diInput.EnumerateFiles("*.ts", SearchOption.AllDirectories))
//						{
//							string inRelativePath = fi.FullName.Substring(diInput.FullName.Length).TrimStart('\\', '/');
//							string outRelativePath = inRelativePath.Remove(inRelativePath.Length - fi.Extension.Length) + ".mp4";
//							DirectoryInfo diOut = new DirectoryInfo(Program.settings.tempOutputDir);
//							DirectoryInfo diDestination = new DirectoryInfo(Program.settings.videoStorageDir);
//							string pathIn = fi.FullName;
//							string pathOut = diOut.FullName.Replace('\\', '/').TrimEnd('/') + '/' + outRelativePath;
//							string pathDestination = diDestination.FullName.Replace('\\', '/').TrimEnd('/') + '/' + outRelativePath;
//							Logger.Debug("HandbrakeAgent: Scheduling file" + Environment.NewLine
//								 + " IN: " + pathIn + Environment.NewLine
//								 + "OUT: " + pathOut + Environment.NewLine
//								 + "DST: " + pathDestination);
//							bool fileReady;
//							while (!(fileReady = CanOpenFileForExclusiveWriting(pathIn)))
//							{
//								if (!File.Exists(pathIn))
//									break;
//								Logger.Debug("Waiting for file to be writable...");
//								Thread.Sleep(5000);
//							}
//							if(fileReady)
//								AddItemToSchedule(pathIn, pathOut, pathDestination);
//							else
//								Logger.Debug("File is no longer found.");
//							break;
//						}
//					}
//					if (taskQueue.Count == 0)
//						Thread.Sleep(60000);
//				}
//			}
//			catch (ThreadAbortException)
//			{
//			}
//			catch (Exception ex)
//			{
//				Logger.Debug(ex);
//			}
//			finally
//			{
//				isActive = false;
//				Logger.Debug("HandbrakeAgent scheduling loop is now exiting");
//			}
//		}

//		private bool CanOpenFileForExclusiveWriting(string path)
//		{
//			try
//			{
//				using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
//				{
//					return true;
//				}
//			}
//			catch { }
//			return false;
//		}
//	}
//}
