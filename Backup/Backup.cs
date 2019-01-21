using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;
using System.Net;
using System.Threading;
using System.Globalization;
using System.Collections;
using System.IO.Compression;

namespace Backup
{
	public class Tools
	{
		public static string GetAppPath()
		{
			return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		}
	}

	public class Config
	{
		private JObject config = JObject.Parse("{}");

		public Config()
		{
			try
			{
				config = JObject.Parse(File.ReadAllText(Tools.GetAppPath() + "\\config.json"));
			}
			catch
			{
				Log.WriteLog("Failed to read config");
			}
		}

		public int GetConfigInt(string key, int defaultVal)
		{
			int res = defaultVal;
			try
			{
				if (config[key] != null)
					res = (int)config[key];
			}
			catch
			{
			}
			return res;
		}

		public string GetConfigString(string key, string defaultVal)
		{
			string res = defaultVal;
			try
			{
				if (config[key] != null)
					res = (string)config[key];
			}
			catch
			{
			}
			return res;
		}
	}

	public class Log
	{
		static String path = Tools.GetAppPath() + "\\log.txt";
		private static object locker = new Object();

		public static void WriteLog(String msg)
		{
			lock (locker)
			{
				try
				{
					using (StreamWriter w = File.AppendText(path))
					{
						w.WriteLine("{0} {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg);
					}
				}
				catch
				{
				}
			}
		}
	}

	public class AsyncData
	{
		public Semaphore semaphore = null;
		public string url = null;
		public string path = null;
		public JArray arr = null;
		public int index = -1;
	}

	public class Data
	{
		public const string fileNameBackup = "info";

		public static JObject GetPatientsData(string url, string id, Hashtable htStudies)
		{
			JObject patient = GetObject(url + "patients/" + id);
			patient["Studies"] = JArray.FromObject(((JArray)patient["Studies"]).OrderBy(val => (string)val).ToArray());
			for (int i = 0; i < ((JArray)patient["Studies"]).Count; i++)
			{
				patient["Studies"][i] = (JObject)htStudies[(string)patient["Studies"][i]];
				JObject study = (JObject)patient["Studies"][i];
				study["Series"] = JArray.FromObject(((JArray)study["Series"]).OrderBy(val => (string)val).ToArray());
				for (int j = 0; j < ((JArray)study["Series"]).Count; j++)
				{
					string seriesID = (string)study["Series"][j];
					JObject series = GetObject(url + "series/" + seriesID);
					study["Series"][j] = series;
					series["Instances"] = JArray.FromObject(((JArray)series["Instances"]).OrderBy(val => (string)val).ToArray());
				}
			}

			return patient;
		}

		public static void Backup(string path, JObject patient, Config config)
		{
			string basePath = path + "_" + (string)patient["ID"] + "\\";
			if (Directory.Exists(basePath))
				Directory.Delete(basePath, true);
			Directory.CreateDirectory(basePath);

			string patientInfo = patient.ToString();

			foreach (JObject study in patient["Studies"])
			{
				string studyPath = basePath + study["ID"] + "\\";
				Directory.CreateDirectory(studyPath);

				foreach (JObject series in study["Series"])
				{
					string seriesPath = studyPath + series["ID"] + "\\";
					Directory.CreateDirectory(seriesPath);

					GetObjectsArr((JArray)series["Instances"], config.GetConfigString("resthost", "http://127.0.0.1:8042/") + "instances", config);
					SaveInstances((JArray)series["Instances"], seriesPath, config);
				}
			}

			string backupPath = path + (string)patient["ID"] + "\\";
			string backupFilePath = path + (string)patient["ID"] + "\\" + (string)patient["ID"] + ".zip";

			if (Directory.Exists(backupPath))
				Directory.Delete(backupPath, true);
			Directory.CreateDirectory(backupPath);

			File.WriteAllText(backupPath + fileNameBackup, patientInfo);
			Compress(basePath, backupFilePath);

			Directory.Delete(basePath, true);
		}

		public static void Compress(string source, string dest)
		{
			ZipFile.CreateFromDirectory(source, dest, CompressionLevel.Optimal, false);
		}

		public static void SaveInstances(JArray instances, string path, Config config)
		{
			string host = config.GetConfigString("resthost", "http://127.0.0.1:8042/");
			int simultaneousNum = config.GetConfigInt("simultaneousThreads", 10);
			Semaphore semaphore = new Semaphore(simultaneousNum, simultaneousNum);
			List<Thread> threads = new List<Thread>(instances.Count);
			foreach (JObject instance in instances)
			{
				try
				{
					AsyncData ad = new AsyncData();
					ad.semaphore = semaphore;
					ad.url = host + "instances/" + instance["ID"] + "/file";
					ad.path = path;
					ad.index = (int)instance["IndexInSeries"];

					Thread thread = new Thread(FileThread);
					threads.Add(thread);
					thread.Start(ad);
				}
				catch (Exception e)
				{
					Log.WriteLog("Failed to get instance data: " + e.Message);
				}
			}

			bool allDone = false;
			while (!allDone)
			{
				bool join = true;
				foreach (Thread thread in threads)
				{
					join = thread.Join(30);
					if (!join)
						break;
				}
				allDone = join;
			}
			threads.Clear();
		}

		private static string GetJSON(string url, byte[] data = null)
		{
			string res = "";

			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
			if (data != null)
			{
				request.Method = "POST";
				request.ContentType = "application/json";
				request.ContentLength = data.Length;
				using (Stream stream = request.GetRequestStream())
				{
					stream.Write(data, 0, data.Length);
				}
			}
			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using (Stream dataStream = response.GetResponseStream())
			{
				StreamReader reader = new StreamReader(dataStream);
				res = reader.ReadToEnd();
				reader.Close();
			}
			response.Close();

			return res;
		}

		public static JArray GetIDs(string url, int period = 0)
		{
			byte[] data = null;
			if (period != 0)
			{
				DateTime dt = DateTime.Now.AddDays(-period + 1);
				data = Encoding.ASCII.GetBytes("{\"Level\":\"Study\",\"Query\":{\"StudyDate\":\"" + dt.ToString("yyyyMMdd") + "-\"}}");
			}
			return JArray.Parse(GetJSON(url, data));
		}

		public static JObject GetObject(string url)
		{
			return JObject.Parse(GetJSON(url));
		}

		public static void SaveFile(string url, string path)
		{
			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using (Stream output = File.OpenWrite(path))
			using (Stream input = response.GetResponseStream())
			{
				input.CopyTo(output);
			}
			response.Close();
		}

		public static JArray GetObjectsArr(JArray arrIDs, string url, Config config)
		{
			int simultaneousNum = config.GetConfigInt("simultaneousThreads", 10);
			Semaphore semaphore = new Semaphore(simultaneousNum, simultaneousNum);
			List<Thread> threads = new List<Thread>(arrIDs.Count);
			for (int i = 0; i < arrIDs.Count; i++)
			{
				try
				{
					AsyncData ad = new AsyncData();
					ad.semaphore = semaphore;
					ad.url = url;
					ad.arr = arrIDs;
					ad.index = i;

					Thread thread = new Thread(DataThread);
					threads.Add(thread);
					thread.Start(ad);
				}
				catch (Exception e)
				{
					Log.WriteLog("Failed to get data: " + e.Message);
				}
			}

			bool allDone = false;
			while (!allDone)
			{
				bool join = true;
				foreach (Thread thread in threads)
				{
					join = thread.Join(30);
					if (!join)
						break;
				}
				allDone = join;
			}
			threads.Clear();

			return arrIDs;
		}

		public static void DataThread(object obj)
		{
			AsyncData adata = obj as AsyncData;
			adata.semaphore.WaitOne();

			try
			{
				adata.arr[adata.index] = GetObject(adata.url + "/" + (string)adata.arr[adata.index]);
			}
			catch (Exception e)
			{
				Log.WriteLog(e.Message);
			}

			adata.semaphore.Release();
		}

		public static void FileThread(object obj)
		{
			AsyncData adata = obj as AsyncData;
			adata.semaphore.WaitOne();

			try
			{
				SaveFile(adata.url, adata.path + string.Format("{0:D4}.dcm", adata.index));
			}
			catch (Exception e)
			{
				Log.WriteLog(e.Message);
			}

			adata.semaphore.Release();
		}
	}

	class Backup
	{
		static Config config = null;

		static void Main(string[] args)
		{
			config = new Config();

			Hashtable htPatientsDB = GetPatientsDB();
			Hashtable htPatientsBackup = GetPatientsBackup();

			JArray exportArr = new JArray();
			string resthost = config.GetConfigString("resthost", "http://127.0.0.1:8042/");
			foreach (string id in htPatientsDB.Keys)
			{
				//if db patient id not in backup - goto backup
				if (!htPatientsBackup.ContainsKey(id))
				{
					try
					{
						exportArr.Add(id);
						JObject patientDB = Data.GetPatientsData(resthost, id, (Hashtable)htPatientsDB[id]);
						Data.Backup(config.GetConfigString("backupDrive", "f:") + "\\", patientDB, config);
					}
					catch
					{

					}
					continue;
				}

				//else compare, if false - goto backup
				try
				{
					JObject patientDB = Data.GetPatientsData(resthost, id, (Hashtable)htPatientsDB[id]);
					JObject patientBackup = JObject.Parse(File.ReadAllText(config.GetConfigString("backupDrive", "f:") + "\\" + id + "\\" + Data.fileNameBackup));
					if (!Compare(patientDB, patientBackup))
					{
						exportArr.Add(id);
						Data.Backup(config.GetConfigString("backupDrive", "f:") + "\\", patientDB, config);
						continue;
					}
				}
				catch
				{
					Log.WriteLog("Failed to get data for compare");
				}
			}

			//add or update descriptions
			ProcessDescriptions();

			//export
			string exportModality = config.GetConfigString("export", "");
			if (exportModality.Length > 0)
			{
				foreach (string id in exportArr)
				{
					try
					{
						Export(resthost, exportModality, id);
					}
					catch (Exception e)
					{
						Log.WriteLog("Export message: " + id + ". " + e.Message);
					}
				}
			}
		}

		public static void Export(string host, string modality, string id)
		{
			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(host + "modalities/" + modality + "/store");
			request.Method = "POST";
			request.ContentType = "text/plain";
			byte[] data = Encoding.ASCII.GetBytes(id);
			request.ContentLength = data.Length;
			request.ReadWriteTimeout = 100000;
			request.Timeout = 30000;
			using (Stream stream = request.GetRequestStream())
			{
				stream.Write(data, 0, data.Length);
			}

			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			if (response.StatusCode != HttpStatusCode.OK)
				Log.WriteLog("Export: bad response from modality");
			response.Close();
		}

		public static void ProcessDescriptions()
		{
			//read ids
			string idsPath = config.GetConfigString("dataDrive", "e:") + "\\id\\";
			string[] ids = new string[0];
			try
			{
				ids = Directory.GetFiles(idsPath);
			}
			catch
			{
				Log.WriteLog("Failed to read ids");
			}

			//read backup folder names
			string backupDrive = config.GetConfigString("backupDrive", "g:\\backup") + "\\";
			string[] dirsBackup = new string[0];
			try
			{
				dirsBackup = Directory.GetDirectories(backupDrive);
			}
			catch
			{
				Log.WriteLog("Failed to read backup folders");
			}

			Hashtable htBackup = new Hashtable(dirsBackup.Length);
			foreach (string dir in dirsBackup)
			{
				string key = Path.GetFileNameWithoutExtension(dir);
				if (!htBackup.ContainsKey(key))
					htBackup.Add(key, dir);
			}

			foreach (string path in ids)
			{
				string key = Path.GetFileNameWithoutExtension(path);
				if (htBackup.ContainsKey(key))
				{
					try
					{
						string desc = File.ReadAllText(path);
						File.WriteAllText(htBackup[key] + "\\description", desc);
					}
					catch
					{
						Log.WriteLog("Failed to write description");
					}
				}
			}
		}

		public static Hashtable GetPatientsDB()
		{
			string host = config.GetConfigString("resthost", "http://127.0.0.1:8042/");
			int time = config.GetConfigInt("period", 30);
			//DateTime minDate = DateTime.Now.AddDays(-time);

			Hashtable htPatientsDB = new Hashtable();

			//get all studies from db
			JArray studies = new JArray();
			try
			{
				studies = Data.GetIDs(host + "tools/find", time);
				studies = Data.GetObjectsArr(studies, host + "studies", config);
			}
			catch
			{
				Log.WriteLog("Failed to get studies");
				return htPatientsDB;
			}

			//filter by min date (already done in request)
			//int i = 0;
			//CultureInfo provider = CultureInfo.InvariantCulture;
			//while (i < studies.Count)
			//{
			//	try
			//	{
			//		DateTime dt = DateTime.ParseExact((string)studies[i]["MainDicomTags"]["StudyDate"], "yyyyMMdd", provider);
			//		if (dt < minDate)
			//		{
			//			studies.RemoveAt(i);
			//			continue;
			//		}
			//	}
			//	catch
			//	{
			//		Log.WriteLog("Failed to parse study");
			//		studies.RemoveAt(i);
			//		continue;
			//	}

			//	i++;
			//}

			//get parent patients ids to hash
			foreach (JObject study in studies)
			{
				try
				{
					if (!htPatientsDB.ContainsKey((string)study["ParentPatient"]))
					{
						Hashtable htStudies = new Hashtable();
						htStudies.Add((string)study["ID"], study);
						htPatientsDB.Add((string)study["ParentPatient"], htStudies);
					}
					else
					{
						((Hashtable)htPatientsDB[(string)study["ParentPatient"]]).Add((string)study["ID"], study);
					}
				}
				catch
				{
					Log.WriteLog("Failed to parse study");
				}
			}

			return htPatientsDB;
		}

		public static Hashtable GetPatientsBackup()
		{
			Hashtable htPatientBackup = new Hashtable();

			int time = config.GetConfigInt("period", 30);
			DateTime minDate = DateTime.Now.AddDays(-time);

			//read all objects from backup
			string backupDrive = config.GetConfigString("backupDrive", "f:") + "\\";
			string[] dirs = new string[0];
			try
			{
				dirs = Directory.GetDirectories(backupDrive);
			}
			catch
			{
				Log.WriteLog("Failed to read backup folders");
				return htPatientBackup;
			}

			JArray patientsBackup = new JArray();
			foreach (string dir in dirs)
			{
				try
				{
					patientsBackup.Add(JObject.Parse(File.ReadAllText(dir + "\\" + Data.fileNameBackup)));
				}
				catch
				{
					Log.WriteLog("Failed to get backup info");
				}
			}

			//filter by min date
			int i = 0;
			CultureInfo provider = CultureInfo.InvariantCulture;
			while (i < patientsBackup.Count)
			{
				JObject patient = (JObject)patientsBackup[i];
				bool match = false;
				foreach (JObject study in patient["Studies"])
				{
					try
					{
						DateTime dt = DateTime.ParseExact((string)study["MainDicomTags"]["StudyDate"], "yyyyMMdd", provider);
						if (dt < minDate)
						{
							patientsBackup.RemoveAt(i);
							match = true;
							break;
						}
					}
					catch
					{
						Log.WriteLog("Failed to parse backup study");
						patientsBackup.RemoveAt(i);
						match = true;
						continue;
					}
				}
				if (match)
					continue;

				i++;
			}

			//get patients ids to hash
			foreach (JObject obj in patientsBackup)
			{
				try
				{
					htPatientBackup.Add((string)obj["ID"], obj);
				}
				catch
				{
					Log.WriteLog("Failed to parse backup patient or add him to hash");
				}
			}

			return htPatientBackup;
		}

		public static bool Compare(JObject patientDB, JObject patientBackup)
		{
			return JToken.DeepEquals(patientDB, patientBackup);
		}
	}
}
