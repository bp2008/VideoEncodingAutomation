using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BPUtil;
using Newtonsoft.Json;

namespace VideoEncodingAutomation
{
	public class HandBrakeTask
	{
		/// <summary>
		/// Path to the input file, relative to the inputDir.
		/// </summary>
		public readonly string inputFileRelativePath;
		public readonly string inputFile;
		public readonly string outputFile;
		public readonly string failedInputFilePath;

		public HandBrakeTask(string inputFileRelativePath, string inputFile, string outputFile, string failedInputFilePath)
		{
			this.inputFileRelativePath = inputFileRelativePath;
			this.inputFile = inputFile;
			this.outputFile = outputFile;
			this.failedInputFilePath = failedInputFilePath;
		}
		public override string ToString()
		{
			return inputFileRelativePath;
		}
	}
	public class HandbrakeStatus
	{
		public volatile bool IsAgentActive = false;
		public volatile bool IsHandbrakeActive = false;
		public volatile bool IsAgentPaused = false;
		public double percentComplete = 0;
		public double fps = 0;
		public double avgFps = 0;
		public TimeSpan ETA = TimeSpan.Zero;
		public HandBrakeTask currentTask = null;
		public string comment = "";
	}
	public class HandbrakeAgent
	{
		private static UTF8Encoding utf8nobom = new UTF8Encoding(false);

		private object scheduleLock = new object();
		private Queue<HandBrakeTask> handBrakeTasks = new Queue<HandBrakeTask>();
		private HashSet<string> currentlyKnownTasks = new HashSet<string>();

		private object startLock = new object();
		private volatile bool started = false;
		private volatile bool abortCurrentHBProcess = false;

		private Thread thrSchedulingThread;
		private Thread thrEncodingHandlerThread;

		private Regex rxEncodingStatus = new Regex(@"Encoding: task \d+ of \d+, (.+?) % \((.+?) fps, avg (.+?) fps, ETA (.+?)\)", RegexOptions.Compiled);
		private Regex rxETA = new Regex(@"(\d\d)h(\d\d)m(\d\d)s", RegexOptions.Compiled);

		public HandbrakeStatus status = new HandbrakeStatus();
		private List<HandBrakeTask> recentlyFinishedTasks = new List<HandBrakeTask>();

		private HashSet<string> allowedExtensionsLower = new HashSet<string>(new string[]
		{
				".ts"
				, ".m2ts"
				, ".mkv"
				, ".mp4"
				, ".avi"
		});

		/// <summary>
		/// The agent is running normally.
		/// </summary>
		public bool Active
		{
			get
			{
				return status.IsAgentActive;
			}
		}

		public void AbortCurrentProcessing()
		{
			if (status.IsHandbrakeActive)
			{
				Logger.Info("HandbrakeAgent: Received command \"Abort\"");
				status.IsAgentPaused = true;
				abortCurrentHBProcess = true;
			}
			else
				Logger.Info("HandbrakeAgent: Received command \"Abort\", but HandBrake is not running.");
		}

		public void Pause()
		{
			Logger.Info("HandbrakeAgent: Received command \"Pause\"");
			status.IsAgentPaused = true;
		}

		public void Unpause()
		{
			Logger.Info("HandbrakeAgent: Received command \"Unpause\"");
			status.IsAgentPaused = false;
		}

		/// <summary>
		/// If the agent is not Active, this may have an error message specifying the reason.
		/// </summary>
		public string ErrorMessage { get; private set; } = "";

		public HandbrakeAgent()
		{
			thrSchedulingThread = new Thread(schedulingLoop);
			thrSchedulingThread.Name = "Scheduling";
			thrSchedulingThread.IsBackground = true;

			thrEncodingHandlerThread = new Thread(encodingLoop);
			thrEncodingHandlerThread.Name = "Encoding Handler";
		}
		public void Start()
		{
			lock (startLock)
			{
				if (started)
					throw new Exception("This instance has already been used!");
				started = true;
			}
			status.IsAgentActive = true;
			thrSchedulingThread.Start();
			thrEncodingHandlerThread.Start();
		}

		public void Shutdown()
		{
			try
			{
				thrSchedulingThread.Abort();
			}
			catch { }
			try
			{
				thrEncodingHandlerThread.Abort();
			}
			catch { }
		}
		/// <summary>
		/// Gets a list of tasks currently scheduled, but not yet being processed.
		/// </summary>
		/// <returns></returns>
		public HandBrakeTask[] GetSnapshotOfSchedule()
		{
			lock (scheduleLock)
			{
				return handBrakeTasks.ToArray();
			}
		}
		public List<HandBrakeTask> GetRecentlyFinishedTasks()
		{
			return recentlyFinishedTasks;
		}
		private void AddItemToSchedule(HandBrakeTask task)
		{
			lock (scheduleLock)
			{
				handBrakeTasks.Enqueue(task);
			}
		}
		private HandBrakeTask GetNextScheduledItem()
		{
			lock (scheduleLock)
			{
				if (handBrakeTasks.Count > 0)
					return handBrakeTasks.Dequeue();
			}
			return null;
		}
		private void schedulingLoop()
		{
			try
			{
				while (status.IsAgentActive)
				{
					try
					{
						// Search for items
						int files = 0;
						lock (scheduleLock)
						{
							files += ScanDirectory(Globals.ApplicationDirectoryBase + "in");
							files += ScanDirectory(Path.Combine(Program.settings.videoStorageDir, "in"));
						}
						Thread.Sleep(Util.Clamp(files * 15000, 15000, 120000));
					}
					catch (ThreadAbortException) { throw; }
					catch (Exception ex)
					{
						Logger.Debug(ex, "Error caught. Pausing scheduler for 1 minute.");
						Thread.Sleep(60000);
					}
				}
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.ToString();
				Logger.Debug(ex);
			}
			finally
			{
				status.IsAgentActive = false;
				Logger.Info("HandbrakeAgent: scheduling loop is now exiting");
			}
		}

		private int ScanDirectory(string path)
		{
			DirectoryInfo diInput = new DirectoryInfo(path);
			if (diInput.Exists)
			{
				FileInfo[] files = diInput.GetFiles("*", SearchOption.AllDirectories)
					.Where(fi => allowedExtensionsLower.Contains(fi.Extension.ToLower()))
					.OrderBy(fi => fi.GetLastWriteTimeUtcAndRepairIfBroken())
					.ToArray();
				foreach (FileInfo fi in files)
				{
					string fiPath = fi.FullName.Replace('\\', '/').TrimEnd('/');
					string inRelativePath = fiPath.Substring(diInput.FullName.Length).TrimStart('/');
					if (!currentlyKnownTasks.Contains(inRelativePath))
					{
						string outRelativePath = (inRelativePath.Remove(inRelativePath.Length - fi.Extension.Length) + ".mkv");

						DirectoryInfo diOut = new DirectoryInfo(Path.Combine(Program.settings.videoStorageDir, "out"));
						DirectoryInfo diFail = new DirectoryInfo(Path.Combine(Program.settings.videoStorageDir, "fail"));

						HandBrakeTask task = new HandBrakeTask(
							inRelativePath
							, fiPath
							, diOut.FullName.Replace('\\', '/').TrimEnd('/') + '/' + outRelativePath
							, diFail.FullName.Replace('\\', '/').TrimEnd('/') + '/' + outRelativePath);
						//Logger.Info("HandbrakeAgent: Scheduling file " + task.inputFileRelativePath);
						AddItemToSchedule(task);
						currentlyKnownTasks.Add(task.inputFileRelativePath);
					}
				}
				return files.Length;
			}
			else
				return 0;
		}

		private void encodingLoop()
		{
			Process process = null;
			try
			{
				while (status.IsAgentActive)
				{
					if (!status.IsAgentPaused)
					{
						HandBrakeTask item = GetNextScheduledItem();
						status.currentTask = item;
						if (item != null)
						{
							try
							{
								// A task is available. Run it.
								Logger.Info("HandbrakeAgent: Initializing job " + item.inputFileRelativePath);
								if (!File.Exists(item.inputFile))
								{
									Logger.Debug("HandbrakeAgent: Input file \"" + item.inputFileRelativePath + "\" is missing. It may have been processed by another agent.");
									continue;
								}

								// Read encoder.txt file
								string firstFolder = item.inputFileRelativePath.Split('/')[0];
								string encoderConfigFolder = item.inputFile.Remove(item.inputFile.Length - item.inputFileRelativePath.Length) + firstFolder + "/";
								if (!Directory.Exists(encoderConfigFolder))
								{
									Logger.Debug("HandbrakeAgent: File \"" + encoderConfigFolder + "\" needs to be in a preset folder containing encoder.txt");
									break;
								}
								string encoderConfigPath = encoderConfigFolder + "encoder.txt";
								if (!File.Exists(encoderConfigPath))
								{
									Logger.Debug("HandbrakeAgent: encoder.txt was expected at \"" + encoderConfigPath + "\" but it was not found. This must be resolved before HandbrakeAgent can continue.");
									break;
								}
								string rawEncoderConfig = File.ReadAllText(encoderConfigPath, utf8nobom);
								EncoderConfig encoderConfig = JsonConvert.DeserializeObject<EncoderConfig>(rawEncoderConfig);
								string encoderConfigValidityError = encoderConfig.CheckValidity();
								if (encoderConfigValidityError != null)
								{
									Logger.Debug("HandbrakeAgent: Config file \"" + encoderConfigPath + "\" was determined to be invalid. This must be resolved manually. Reason given:" + encoderConfigValidityError);
									break;
								}
								FileInfo fiLocalEncoderConfigCopy = new FileInfo(Globals.ApplicationDirectoryBase + "in/" + firstFolder + "/encoder.txt");
								if (!fiLocalEncoderConfigCopy.Directory.Exists)
									Directory.CreateDirectory(fiLocalEncoderConfigCopy.Directory.FullName);
								File.WriteAllText(fiLocalEncoderConfigCopy.FullName, rawEncoderConfig, utf8nobom);

								FileInfo fiLocalInput = new FileInfo(Globals.ApplicationDirectoryBase + "in/" + item.inputFileRelativePath);
								if (!Directory.Exists(fiLocalInput.Directory.FullName))
									Directory.CreateDirectory(fiLocalInput.Directory.FullName);

								// Obtain exclusive write access
								if (!PathsEqual(item.inputFile, fiLocalInput.FullName))
								{
									Logger.Info("HandbrakeAgent: Obtaining exclusive access to " + item.inputFileRelativePath);
									string lockFilePath = item.inputFile + ".halock";
									try
									{
										using (FileStream fsLockFile = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
										{
											fsLockFile.Lock(0, 0);
											try
											{
												LockFile lf = LockFile.Read(fsLockFile);
												if (lf != null)
												{
													Logger.Debug("HandbrakeAgent: Unable to lock \"" + item.inputFileRelativePath + "\" because a valid halock file already existed.");
													continue;
												}
												fsLockFile.Seek(0, SeekOrigin.Begin);
												fsLockFile.SetLength(0);
												lf = LockFile.CreateNew();
												LockFile.Write(fsLockFile, lf);

												Thread.Sleep(5000);

												fsLockFile.Seek(0, SeekOrigin.Begin);
												lf = LockFile.Read(fsLockFile);
												if (lf == null)
												{
													Logger.Debug("HandbrakeAgent: Unable to lock \"" + item.inputFileRelativePath + "\" because it could not write a valid halock file.");
													continue;
												}
												if (lf.MachineName != Environment.MachineName)
												{
													Logger.Debug("HandbrakeAgent: Unable to lock \"" + item.inputFileRelativePath + "\" because another machine (\"" + lf.MachineName + "\") claimed it at " + lf.Timestamp);
													continue;
												}

												// Wait for file to be writable
												TimeSpan maxWaitTime = TimeSpan.FromSeconds(30);
												Stopwatch sw = Stopwatch.StartNew();
												bool fileReady = false;
												while (status.IsAgentActive
													&& !(fileReady = CanOpenFileForExclusiveWriting(item.inputFile)))
												{
													if (!File.Exists(item.inputFile))
													{
														Logger.Debug("HandbrakeAgent: Input file \"" + item.inputFileRelativePath + "\" is missing. It may have been processed by another agent.");
														break;
													}
													if (sw.Elapsed > maxWaitTime)
													{
														Logger.Info("HandbrakeAgent: Input file \"" + item.inputFileRelativePath + "\" is not writable within a reasonable amount of time. Moving it to the back of the schedule.");
														AddItemToSchedule(item);
														break;
													}
													TimeSpan remaining = (maxWaitTime - sw.Elapsed);
													Logger.Info("HandbrakeAgent: Waiting up to " + remaining + " for file to be writable...");
													TimeSpan waitFor = remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5);
													Thread.Sleep(waitFor);
												}
												if (!fileReady)
													continue;

												Logger.Info("HandbrakeAgent: Moving " + item.inputFileRelativePath + " to local temp directory");

												File.Move(item.inputFile, fiLocalInput.FullName);
											}
											catch (Exception ex)
											{
												Logger.Debug(ex);
												continue;
											}
										}
									}
									catch
									{
										// Unable to obtain exclusive write access
										Logger.Info("HandbrakeAgent: Input file \"" + item.inputFileRelativePath + "\" is already in use. It may have been processed by another agent.");
										continue;
									}
									finally
									{
										Try.Swallow(() =>
										{
											if (File.Exists(lockFilePath))
												File.Delete(lockFilePath);
										});
									}
								}

								Logger.Info("HandbrakeAgent: Preparing to encode " + item.inputFileRelativePath);

								FileInfo fiOut = new FileInfo(item.outputFile);
								if (!fiOut.Directory.Exists)
									Directory.CreateDirectory(fiOut.Directory.FullName);
								if (fiOut.Exists)
								{
									Logger.Debug("HandbrakeAgent: Output file already exists; please deal with this! \"" + fiOut.FullName + "\"");
									break;
								}

								string tempOutFile = Globals.ApplicationDirectoryBase + fiOut.Name;
								if (File.Exists(tempOutFile))
									File.Delete(tempOutFile);

								if (!status.IsAgentActive)
									break;


								//FileInfo fiFinalDestination = new FileInfo(item.destinationFile);
								//if (!fiFinalDestination.Directory.Exists)
								//	Directory.CreateDirectory(fiFinalDestination.Directory.FullName);
								//if (fiFinalDestination.Exists)
								//{
								//	Logger.Debug("HandbrakeAgent: Destination file already exists; please deal with this! " + fiFinalDestination.FullName);
								//	continue;
								//}
								FileInfo fiFailedDestination = new FileInfo(item.failedInputFilePath);
								if (!fiFailedDestination.Directory.Exists)
									Directory.CreateDirectory(fiFailedDestination.Directory.FullName);



								HandbrakeConfigManager handbrakeConfig = new HandbrakeConfigManager(fiLocalInput.FullName, tempOutFile, encoderConfig);
								string args = handbrakeConfig.GetHandbrakeArgs();

								// Write a record of this conversion attempt
								if (!Directory.Exists(Globals.ApplicationDirectoryBase + "MediaInfo"))
									Directory.CreateDirectory(Globals.ApplicationDirectoryBase + "MediaInfo");
								string path = Globals.ApplicationDirectoryBase + "MediaInfo/" + fiLocalInput.Name + " " + TimeUtil.GetTimeInMsSinceEpoch() + ".info";
								string infoText = "" + fiLocalInput.Name + "\r\n"
									+ "Encoding began at [" + DateTime.Now
									+ "] with configuration \"" + firstFolder + "\" using Handbrake Args:\r\n" + args + "\r\n\r\n\r\n"
									+ "MediaInfo:\r\n" + (handbrakeConfig.mediaInfo == null ? "null" : handbrakeConfig.mediaInfo.json);
								File.WriteAllText(path, infoText, utf8nobom);

								// Begin the encode
								Logger.Info("HandbrakeAgent: Beginning to encode " + fiLocalInput.Name + Environment.NewLine + "Args: " + args);

								// "C:\Program Files\Handbrake\HandBrakeCLI.exe" -i "C:\Source\Video.ts" -o "out.mp4" -e x264 -q 20 -E copy -O

								ProcessStartInfo psi = new ProcessStartInfo(Program.settings.handbrakePath, args);

								//+ " -e " + (Program.settings.h265 ? "x265" : "x264")
								//+ " -q " + Program.settings.quality
								//+ " -E copy"
								//+ " -O"
								////+ " --all-audio" // All audio tracks
								////+ " --all-subtitles" // All subtitle tracks
								////+ " --native-language eng" // English only
								////+ " --native-dub" // English only
								////+ " -w 1920"
								////+ " -l 1080"
								////+ " -m" // Add chapter markers
								//+ " --crop 0:0:0:0" // No cropping
								//+ " --modulus 2"
								//+ " --encoder-preset medium");
								psi.CreateNoWindow = true;
								psi.UseShellExecute = false;
								psi.RedirectStandardOutput = true;
								psi.RedirectStandardError = true;

								process = new Process();
								process.StartInfo = psi;
								abortCurrentHBProcess = false;
								status.comment = "";
								status.IsHandbrakeActive = true;
								process.Start();
								process.PriorityClass = ProcessPriorityClass.BelowNormal;

								bool readStdOutFinished = false;
								bool readStdErrFinished = false;
								bool lastEncodeDoneSet = false;
								StringBuilder sbStdErr = new StringBuilder();
								int handbrakeStdOutLines = 0;
								int handbrakeStdErrLines = 0;
								Thread thrReadStdOut = new Thread(() =>
								{
									try
									{
										do
										{
											string line = process?.StandardOutput?.ReadLine();
											if (line == null)
												break;
											handbrakeStdOutLines++;
											ProcessHandbrakeStandardOutputLine(line);
											//File.AppendAllText(Program.settings.handbrakeStdOutPath, DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt") + "\t" + line + Environment.NewLine);
										}
										while (!abortCurrentHBProcess);
									}
									catch (Exception ex)
									{
										if (!abortCurrentHBProcess)
											Logger.Debug(ex);
									}
									readStdOutFinished = true;
								});
								Thread thrReadStdErr = new Thread(() =>
								{
									try
									{
										do
										{
											string line = process?.StandardError?.ReadLine();
											if (line == null)
												break;
											else if (line == "Encode done!")
												lastEncodeDoneSet = true;
											sbStdErr.AppendLine(line);
											handbrakeStdErrLines++;
											File.AppendAllText("HandbrakeStdErr.txt", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt") + "\t" + line + Environment.NewLine);
										}
										while (!abortCurrentHBProcess);
									}
									catch (Exception ex)
									{
										if (!abortCurrentHBProcess)
											Logger.Debug(ex);
									}
									readStdErrFinished = true;
								});
								thrReadStdOut.Name = "StdOutReader";
								thrReadStdErr.Name = "StdErrReader";
								thrReadStdOut.Start();
								thrReadStdErr.Start();

								while (status.IsAgentActive
									&& !abortCurrentHBProcess
									&& !process.WaitForExit(500)) ;

								if (abortCurrentHBProcess)
								{
									TryAbortHBProc(ref process);
									for (int i = 0; i < 4; i++)
									{
										if (!readStdOutFinished || !readStdErrFinished)
											Thread.Sleep(50);
									}
									abortCurrentHBProcess = false;
									continue;
								}


								if (!status.IsAgentActive)
									return;

								status.IsHandbrakeActive = false;

								if (!readStdOutFinished || !readStdErrFinished)
								{
									Logger.Info("HandbrakeAgent: Waiting for output streams to finish.");
									while (!readStdOutFinished || !readStdErrFinished)
									{
										Thread.Sleep(50);
									}
								}

								if (!lastEncodeDoneSet)
								{
									Logger.Info("HandbrakeAgent: Moving \"" + fiLocalInput.FullName + "\" to FAILED directory \"" + fiFailedDestination.FullName + "\"");
									if (!encoderConfig.KeepInputForDebuggingAfterward)
										fiLocalInput.MoveTo(fiFailedDestination.FullName);

									Logger.Info("HandbrakeAgent: Handbrake did not indicate successful completion!");
									File.AppendAllText(fiOut.Directory.FullName.Replace('\\', '/').TrimEnd('/') + "/HANDBRAKE-LOG-" + fiOut.Name + ".txt"
										, "There is reason to believe Handbrake has failed to encode \"" + fiOut.Name + "\" at " + DateTime.Now.ToString() + "." + Environment.NewLine
										+ "Likely, the source file was damaged.  "
										+ (encoderConfig.KeepInputForDebuggingAfterward ? "" : ("It has been moved so you can inspect it: \"" + fiFailedDestination.FullName + "\""))
										+ Environment.NewLine
										+ "This is a Handbrake log for the supposedly failed encoding process." + Environment.NewLine + sbStdErr.ToString());
								}

								if (!File.Exists(tempOutFile))
								{
									Logger.Debug("HandbrakeAgent: Expected output file " + tempOutFile
										+ " does not exist!  Did handbrake run correctly?" + Environment.NewLine
										+ "Handbrake wrote " + handbrakeStdOutLines + " lines to Standard Output and "
										+ handbrakeStdErrLines + " lines to Standard Error (all non-status output goes here)."
										+ Environment.NewLine
										+ "You may view the StdErr output in the file \"HandbrakeStdErr.txt\""
										);
									return;
								}
								else
								{
									fiOut.Refresh();
									if (fiOut.Exists)
									{
										Logger.Debug("HandbrakeAgent: Output file already exists; please deal with this! \"" + fiOut.FullName + "\"");
										continue;
									}
									Logger.Info("HandbrakeAgent: Moving \"" + tempOutFile + "\" to \"" + fiOut.FullName + "\"");
									File.Move(tempOutFile, fiOut.FullName);

									if (lastEncodeDoneSet)
									{
										// TODO: Re-enable deletion.
										if (!encoderConfig.KeepInputForDebuggingAfterward)
										{
											Logger.Info("HandbrakeAgent: Deleting " + fiLocalInput.FullName);

											fiLocalInput.Delete();
										}
									}

									Logger.Info("HandbrakeAgent: Done with " + fiLocalInput.Name);

									recentlyFinishedTasks.Add(item);
								}

							}
							finally
							{
								lock (scheduleLock)
								{
									currentlyKnownTasks.Remove(item.inputFileRelativePath);
								}
								status.currentTask = null;
								status.avgFps = status.fps = status.percentComplete = 0;
								status.comment = "";
								status.ETA = TimeSpan.FromSeconds(-1);
							}
						}
						else
							Thread.Sleep(1000);
					}
					else
						Thread.Sleep(250);
				}
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.ToString();
				Logger.Debug(ex);
			}
			finally
			{
				TryAbortHBProc(ref process);
				status.IsAgentActive = false;
				Logger.Info("HandbrakeAgent: encoding loop is now exiting");
			}
		}

		private bool TryAbortHBProc(ref Process process)
		{
			try
			{
				if (process != null && !process.HasExited)
				{
					Logger.Info("HandbrakeAgent: Killing HandBrake process.");
					process.Kill();
					process = null;
				}
				status.IsHandbrakeActive = false;
				status.currentTask = null;
				return true;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex);
			}
			return false;
		}

		private void ProcessHandbrakeStandardOutputLine(string line)
		{
			if (line.StartsWith("Encoding: task"))
			{
				Match m = rxEncodingStatus.Match(line);
				if (m.Success)
				{
					status.percentComplete = Util.ToDouble(m.Groups[1].Value);
					status.fps = Util.ToDouble(m.Groups[2].Value);
					status.avgFps = Util.ToDouble(m.Groups[3].Value);

					m = rxETA.Match(m.Groups[4].Value);
					if (m.Success)
						status.ETA = new TimeSpan(Util.ToInt(m.Groups[1].Value), Util.ToInt(m.Groups[2].Value), Util.ToInt(m.Groups[3].Value));
					else
						status.ETA = TimeSpan.FromSeconds(-1);

					if (line.Contains("Muxing: this may take awhile"))
						status.comment = line;
					else if (status.percentComplete == 100)
						status.comment = "Note: Handbrake can spend several minutes at 100% progress.";
					else
						status.comment = "";
				}
			}
		}

		private bool CanOpenFileForExclusiveWriting(string path)
		{
			try
			{
				using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
				{
					return true;
				}
			}
			catch { }
			return false;
		}
		private bool PathsEqual(string pathA, string pathB)
		{
			pathA = Path.GetFullPath(pathA);
			pathB = Path.GetFullPath(pathB);
			return string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase);
		}
	}
}
