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
			if (p.Request.Page == "")
			{
				FileInfo fi = new FileInfo(Program.isDebug ? "../../default.html" : (Globals.ApplicationDirectoryBase + "default.html"));
				p.Response.StaticFile(fi);
				return;
			}
			else if (p.Request.Page == "jquery-3.1.1.min.js")
			{
				FileInfo fi = new FileInfo(Program.isDebug ? "../../jquery-3.1.1.min.js" : (Globals.ApplicationDirectoryBase + "jquery-3.1.1.min.js"));
				p.Response.StaticFile(fi);
				return;
			}

			if (p.Request.Page == "log")
			{
				int lastLineLoaded = p.Request.GetIntParam("l", -1);
				long readerId = p.Request.GetLongParam("i");
				List<string> logUpdate = slogReader.GetLogUpdate(readerId, ref lastLineLoaded);
				string json = JsonConvert.SerializeObject(new LogUpdateResponse(logUpdate, lastLineLoaded));
				p.Response.FullResponseUTF8(json, "application/json; charset=UTF-8");
			}
			else if (p.Request.Page == "logReaderId")
			{
				p.Response.FullResponseUTF8(slogReader.readerId.ToString(), "text/plain; charset=UTF-8");
			}
			else if (p.Request.Page == "status")
			{
				string json = JsonConvert.SerializeObject(ha.status);
				p.Response.FullResponseUTF8(json, "application/json; charset=UTF-8");
			}
			else if (p.Request.Page == "slowstatus")
			{
				SlowStatus status = UpdateSlowStatus();
				string json = JsonConvert.SerializeObject(status);
				p.Response.FullResponseUTF8(json, "application/json; charset=UTF-8");
			}
			else if (p.Request.Page == "restart_handbrake_agent")
			{
				if (ha.Active)
					p.Response.FullResponseUTF8("0", "text/plain; charset=UTF-8");
				else
				{
					ha.Shutdown();
					ha = new HandbrakeAgent();
					ha.Start();
					p.Response.FullResponseUTF8("0", "text/plain; charset=UTF-8");
				}
			}
			else if (p.Request.Page == "abort_handbrake_processing")
			{
				ha.AbortCurrentProcessing();
				p.Response.FullResponseUTF8("", "text/plain; charset=UTF-8");
			}
			else if (p.Request.Page == "pause_handbrake_processing")
			{
				ha.Pause();
				p.Response.FullResponseUTF8("", "text/plain; charset=UTF-8");
			}
			else if (p.Request.Page == "unpause_handbrake_processing")
			{
				ha.Unpause();
				p.Response.FullResponseUTF8("", "text/plain; charset=UTF-8");
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
