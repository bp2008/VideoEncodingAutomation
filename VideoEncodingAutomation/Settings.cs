﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BPUtil;

namespace VideoEncodingAutomation
{
	public class Settings : SerializableObjectBase
	{
		public string videoStorageDir = @"C:\Encode\";
		public string handbrakePath = @"C:\Program Files\Handbrake\HandBrakeCLI.exe";
		public int webPort = 14580;
		public string mediaInfoCLIPath = "MediaInfo.exe";
	}

	public abstract class SerializableObjectBase
	{
		public bool Save(string filePath = null)
		{
			try
			{
				lock (this)
				{
					if (filePath == null)
						filePath = this.GetType().Name + ".cfg";
					FileInfo fi = new FileInfo(filePath);
					if (!fi.Exists)
					{
						if (!fi.Directory.Exists)
							Directory.CreateDirectory(fi.Directory.FullName);
					}
					System.Xml.Serialization.XmlSerializer x = new System.Xml.Serialization.XmlSerializer(this.GetType());
					using (FileStream fs = new FileStream(filePath, FileMode.Create))
						x.Serialize(fs, this);
				}
				return true;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex);
			}
			return false;
		}
		public bool Load(string filePath = null)
		{
			try
			{
				Type thistype = this.GetType();
				if (filePath == null)
					filePath = thistype.Name + ".cfg";
				lock (this)
				{
					if (!File.Exists(filePath))
						return false;
					System.Xml.Serialization.XmlSerializer x = new System.Xml.Serialization.XmlSerializer(this.GetType());
					object obj;
					using (FileStream fs = new FileStream(filePath, FileMode.Open))
						obj = x.Deserialize(fs);
					foreach (FieldInfo sourceField in obj.GetType().GetFields())
					{
						try
						{
							FieldInfo targetField = thistype.GetField(sourceField.Name);
							if (targetField != null && targetField.MemberType == sourceField.MemberType)
								targetField.SetValue(this, sourceField.GetValue(obj));
						}
						catch (Exception) { }
					}
				}
				return true;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex);
			}
			return false;
		}
	}
}
