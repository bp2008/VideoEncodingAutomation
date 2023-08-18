using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BPUtil;
using BPUtil.SimpleHttp;
using Newtonsoft.Json;

namespace VideoEncodingAutomation
{
	public class WebServer : HttpServer
	{
		HandbrakeAgent ha = new HandbrakeAgent();
		StreamingLogReader slogReader = new StreamingLogReader(null, 200);
		SlowStatus _slowStatus = new SlowStatus();

		public WebServer(X509Certificate2 cert = null) : base(SimpleCertificateSelector.FromCertificate(cert))
		{
			ha.Start();
			slogReader.Start();
		}

		public override void handleGETRequest(HttpProcessor p)
		{
			if (p.requestedPage == "")
			{
				FileInfo fi = new FileInfo(Program.isDebug ? "../../default.html" : (Globals.ApplicationDirectoryBase + "default.html"));
				if (fi.Exists)
				{
					p.writeSuccess(contentLength: fi.Length);
					p.outputStream.Flush();
					using (Stream fiStr = fi.OpenRead())
						fiStr.CopyTo(p.tcpStream);
					p.tcpStream.Flush();
				}
				return;
			}
			else if (p.requestedPage == "jquery-3.1.1.min.js")
			{
				FileInfo fi = new FileInfo(Program.isDebug ? "../../jquery-3.1.1.min.js" : (Globals.ApplicationDirectoryBase + "jquery-3.1.1.min.js"));
				if (fi.Exists)
				{
					p.writeSuccess("text/javascript; charset=UTF-8", contentLength: fi.Length);
					p.outputStream.Flush();
					using (Stream fiStr = fi.OpenRead())
						fiStr.CopyTo(p.tcpStream);
					p.tcpStream.Flush();
				}
				return;
			}

			if (p.requestedPage == "log")
			{
				int lastLineLoaded = p.GetIntParam("l", -1);
				long readerId = p.GetLongParam("i");
				List<string> logUpdate = slogReader.GetLogUpdate(readerId, ref lastLineLoaded);
				string json = JsonConvert.SerializeObject(new LogUpdateResponse(logUpdate, lastLineLoaded));
				p.writeSuccess("application/json; charset=UTF-8");
				p.outputStream.Write(json);
			}
			else if (p.requestedPage == "logReaderId")
			{
				p.writeSuccess("text/plain; charset=UTF-8");
				p.outputStream.Write(slogReader.readerId.ToString());
			}
			else if (p.requestedPage == "status")
			{
				string json = JsonConvert.SerializeObject(ha.status);
				p.writeSuccess("application/json; charset=UTF-8");
				p.outputStream.Write(json);
			}
			else if (p.requestedPage == "slowstatus")
			{
				SlowStatus status = UpdateSlowStatus();
				string json = JsonConvert.SerializeObject(status);
				p.writeSuccess("application/json; charset=UTF-8");
				p.outputStream.Write(json);
			}
			else if (p.requestedPage == "restart_handbrake_agent")
			{
				p.writeSuccess("text/plain; charset=UTF-8");
				if (ha.Active)
					p.outputStream.Write("0");
				else
				{
					ha.Shutdown();
					ha = new HandbrakeAgent();
					ha.Start();
					p.outputStream.Write("1");
				}
			}
			else if (p.requestedPage == "abort_handbrake_processing")
			{
				ha.AbortCurrentProcessing();
				p.writeSuccess();
			}
			else if (p.requestedPage == "pause_handbrake_processing")
			{
				ha.Pause();
				p.writeSuccess();
			}
			else if (p.requestedPage == "unpause_handbrake_processing")
			{
				ha.Unpause();
				p.writeSuccess();
			}
		}

		public override void handlePOSTRequest(HttpProcessor p)
		{
		}

		protected override void stopServer()
		{
			ha.Shutdown();
		}

		/// <summary>
		/// A class containing status that does not need to be updated for the user particularly quickly.
		/// </summary>
		private class SlowStatus
		{
			public string[] queuedTasks;
			public string[] recentlyFinishedTasks;
		}
		private SlowStatus UpdateSlowStatus()
		{
			_slowStatus.queuedTasks = ha.GetSnapshotOfSchedule().Select(t => t.inputFileRelativePath).ToArray();
			_slowStatus.recentlyFinishedTasks = ha.GetRecentlyFinishedTasks().Select(t => t.inputFileRelativePath).Reverse().ToArray();
			//_slowStatus.recentlyFinishedTasks = new string[] { "Finished 1", "Finished 2", "Finished 3", "Finished 4", "Finished 5" };
			//_slowStatus.queuedTasks = new string[] { "Queued 1", "Queued 2", "Queued 3" };
			return _slowStatus;
		}
		private class LogUpdateResponse
		{
			public int lastLineLoaded;
			public List<string> logUpdate;
			public LogUpdateResponse(List<string> logUpdate, int lastLineLoaded)
			{
				this.logUpdate = logUpdate;
				this.lastLineLoaded = lastLineLoaded;
			}
		}
	}
}
