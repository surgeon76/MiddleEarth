using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Services;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.IO.Compression;
using System.Globalization;
using System.Collections;
using Newtonsoft.Json;

namespace MiddleEarth
{
	public class AsyncData
	{
		public Semaphore semaphore = null;
		public string url = null;
		public int index = -1;
		public List<Study> studies = null;
	}

	public class Study
	{
		public string patient = "";
		public string study = "";

		public static String GetStudiesIDs(String host, int period = 0)
		{
			String responseFromServer = "{[]}";

			WebRequest request = WebRequest.Create(period != 0 ? host + "tools/find": host + "studies");
			if (period != 0)
			{
				DateTime dt = DateTime.Now.AddDays(-period + 1);
				byte[] data = Encoding.ASCII.GetBytes("{\"Level\":\"Study\",\"Query\":{\"StudyDate\":\"" + dt.ToString("yyyyMMdd") + "-\"}}");
				request.Method = "POST";
				request.ContentType = "application/json";
				request.ContentLength = data.Length;
				using (Stream stream = request.GetRequestStream())
				{
					stream.Write(data, 0, data.Length);
				}
			}
			WebResponse response = request.GetResponse();
			using (Stream dataStream = response.GetResponseStream())
			{
				StreamReader reader = new StreamReader(dataStream);
				responseFromServer = reader.ReadToEnd();
				reader.Close();
			}
			response.Close();

			return responseFromServer;
		}

		public static void DataThread(object obj)
		{
			AsyncData adata = obj as AsyncData;
			adata.semaphore.WaitOne();

			try
			{
				WebRequest request = WebRequest.Create(adata.url);
				WebResponse response = request.GetResponse();
				using (Stream stream = response.GetResponseStream())
				{
					StreamReader reader = new StreamReader(stream);
					String responseString = reader.ReadToEnd();

					if (adata.index < 0)
					{
						Study study = new Study();
						study.study = responseString;
						lock (adata.studies)
						{
							adata.studies.Add(study);
						}
					}
					else
					{
						lock (adata.studies[adata.index].patient)
						{
							adata.studies[adata.index].patient = responseString;
						}
					}
				}
				response.Close();
			}
			catch (Exception e)
			{
				Log.WriteLog(e.Message);
			}

			adata.semaphore.Release();
		}

		public string GetJSON()
		{
			return "{\"patient\":" + patient + ",\"study\":" + study + "}";
		}
	}

	public class Log
	{
		static String path = HttpContext.Current.Server.MapPath("~") + "\\log.txt";
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

	public partial class MiddleEarthMainForm : System.Web.UI.Page
	{
		Config config = new Config();

		protected void Page_Load(object sender, EventArgs e)
		{
			if (!IsPostBack)
			{
				Response.Clear();

                Response.AddHeader("Cache-Control", "no-cache");
				Response.Expires = 0;
				Response.Cache.SetNoStore();
				Response.AddHeader("Pragma", "no-cache");

				ReadConfig();
				if (Request.QueryString["source"] != null)
					config.source = Request.QueryString["source"];
				else
					config.source = (string)((JArray)config.config["sources"])[0];

				if (Request.QueryString["request"] == "list")
				{
					Response.Write(List());
				}
				else if (Request.QueryString["request"] == "download")
				{
					Response.Write(Download());
                    DownloadTools.ClearTempFolder(config.GetConfigInt("tempFilesLifeTime", 20));
				}
				else if (Request.QueryString["request"] == "config")
				{
					Response.Write(GetClientConfig());
				}
				else if (Request.QueryString["request"] == "delete")
				{
					Response.Write(Delete());
				}
				else if (Request.QueryString["request"] == "space")
				{
					Response.Write(GetFreeDiskSpace());
				}
				else if (Request.QueryString["request"] == "backupSpace")
				{
					Response.Write(GetFreeDiskSpace("backupDrive"));
				}
				else if (Request.QueryString["request"] == "savedesc")
				{
					Response.Write(SaveDesc());
				}
				else
				{
					return;
				}

				Response.End();
			}
		}

		protected string SaveDesc()
		{
			string res = "{\"error\":\"failed\"}";

			if (!IsAdmin())
				return "{\"error\":\"permissions\"}";

			IDDesc iddesc = new IDDesc();
			iddesc.id = Request.QueryString["id"];
			iddesc.desc = Request.QueryString["desc"];
			if (iddesc.Save(config.GetConfigSourceOption("dataDrive") + "\\"))
				res = "{\"ok\":\"done\"}";

			return res;
		}

		protected string Delete()
		{
			string res = "{\"error\":\"failed\"}";

			if (!IsAdmin())
				return "{\"error\":\"permissions\"}";

			Log.WriteLog("Delete operation has been requested: " + (Request.QueryString["patient"] == "true" ? "patient " : "study ") + Request.QueryString["id"] + " " + Request.UserHostAddress);

			try
			{
				//check time
				List<JObject> studies = new List<JObject>();
				if (Request.QueryString["patient"] == "true")
				{
					JObject jpatient = JObject.Parse(DownloadTools.GetJSON(config.GetConfigSourceOption("resthost"), "patients", Request.QueryString["id"]));
					JArray jstudies = (JArray)jpatient["Studies"];
					foreach (string study in jstudies)
					{
						studies.Add(JObject.Parse(DownloadTools.GetJSON(config.GetConfigSourceOption("resthost"), "studies", study)));
					}
				}
				else
				{
					studies.Add(JObject.Parse(DownloadTools.GetJSON(config.GetConfigSourceOption("resthost"), "studies", Request.QueryString["id"])));
				}

				CultureInfo provider = CultureInfo.InvariantCulture;
				foreach (JObject study in studies)
				{
					TimeSpan ts = DateTime.Now - DateTime.ParseExact((string)study["MainDicomTags"]["StudyDate"], "yyyyMMdd", provider);
					if (ts.TotalHours > config.GetConfigInt("deleteTime", 36))
					{
						res = "{\"error\":\"expired\"}";

						Log.WriteLog("Delete operation time has been expired: " + (Request.QueryString["patient"] == "true" ? "patient " : "study ") + Request.QueryString["id"]);

						return res;
					}
				}

				Log.WriteLog("Delete operation has been started: " + (Request.QueryString["patient"] == "true" ? "patient " : "study ") + Request.QueryString["id"]);

				//delete
				HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(config.GetConfigSourceOption("resthost") + (Request.QueryString["patient"] == "true" ? "patients/" : "studies/") + Request.QueryString["id"]);
				request.Timeout = config.GetConfigInt("restTimeout", 300000);
				request.Method = "DELETE";
				HttpWebResponse response = (HttpWebResponse)request.GetResponse();
				if (response.StatusCode == HttpStatusCode.OK)
					res = "{\"ok\":\"done\"}";

				Log.WriteLog("Delete operation has been completed: " + (Request.QueryString["patient"] == "true" ? "patient " : "study ") + Request.QueryString["id"]);
			}
			catch
			{
				Log.WriteLog("Failed to delete " + (Request.QueryString["patient"] == "true" ? "patient " : "study ") + Request.QueryString["id"]);
			}
			return res;
		}

		protected string StudiesList(string host)
		{
            int simultaneousNum = config.GetConfigInt("simultaneousThreads", 10);
			int period = 0;
			try
			{
				period = int.Parse(Request.QueryString["period"]);
			}
			catch
			{
			}

			string[] studyIDs = new string[0];
			try
			{
				studyIDs = JArray.Parse(Study.GetStudiesIDs(host, period)).ToObject<string[]>();
			}
			catch
			{
				Log.WriteLog("Failed to get studies' IDs");
			}

			Semaphore semaphore = new Semaphore(simultaneousNum, simultaneousNum);

			List<Study> list = new List<Study>(studyIDs.Length);
			List<Thread> threads = new List<Thread>(studyIDs.Length);
			foreach (string item in studyIDs)
			{
				try
				{
					AsyncData ad = new AsyncData();
					ad.semaphore = semaphore;
					ad.url = host + "studies/" + item;
					ad.studies = list;
					ad.index = -1;

					Thread thread = new Thread(Study.DataThread);
					threads.Add(thread);
					thread.Start(ad);
				}
				catch (Exception e)
				{
					Log.WriteLog("Failed to get study " + item + ": " + e.Message);
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

			semaphore = new Semaphore(simultaneousNum, simultaneousNum);

			threads = new List<Thread>(studyIDs.Length);
			for (int i = 0; i < list.Count; i++)
			{
				string parent = "";
				try
				{
					AsyncData ad = new AsyncData();
					ad.semaphore = semaphore;
					parent = (string)JObject.Parse(list[i].study)["ParentPatient"];
					ad.url = host + "patients/" + parent;
					ad.studies = list;
					ad.index = i;

					Thread thread = new Thread(Study.DataThread);
					threads.Add(thread);
					thread.Start(ad);
				}
				catch (Exception e)
				{
					Log.WriteLog("Failed to get study's parent patient " + parent + ": " + e.Message);
				}
			}

			allDone = false;
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

			//append description
			Hashtable ht = IDDesc.Load(config.GetConfigSourceOption("dataDrive") + "\\");
			try
			{
				for (int i = 0; i < list.Count; i++)
				{
					try
					{
						JObject jpatient = JObject.Parse(list[i].patient);
						//string id = (string)jpatient["MainDicomTags"]["PatientID"];
						string id = (string)jpatient["ID"];
						if (ht.ContainsKey(id))
						{
							string escid = JsonConvert.ToString(ht[id]);
							list[i].patient = list[i].patient.Insert(list[i].patient.LastIndexOf('}'), ",\"iddesc\":" + escid);
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}

			StringBuilder json = new StringBuilder();
			json.Append("[");
			if (list.Count > 0)
				json.Append(list[0].GetJSON());
			for (int i = 1; i < list.Count; i++)
			{
				json.Append("," + list[i].GetJSON());
			}
			json.Append("]");

			return json.ToString();
		}

		protected string List(string source = null)
		{
			return StudiesList(config.GetConfigSourceOption("resthost"));
		}

		protected string Download()
		{
			string fname = DownloadTools.Download(config, Request.QueryString["patient"], Request.QueryString["study"], Request.QueryString["radiant"] == "true");
			if (fname.Length == 0)
				return "{\"error\":\"failed\"}";

			return "{\"path\":\"" + DownloadTools.GetTempUrl() + fname + "\"}";
		}

		protected void ReadConfig()
		{
			try
			{
				config.config = JObject.Parse(File.ReadAllText(HttpContext.Current.Server.MapPath("~") + "config.json"));
			}
			catch
			{
				Log.WriteLog("Failed to read config");
			}
		}

		protected string GetClientConfig()
		{
			JObject toClient = new JObject();

			try
			{
				toClient.Add("sources", config.config["sources"]);
				for (int i = 0; i < ((JArray)config.config["sources"]).Count; i++)
				{
					string key = (string)((JArray)config.config["sources"])[i];
					toClient.Add(key, config.config[key]);
				}
				toClient.Add("admin", IsAdmin() ? "true" : "false");
			}
			catch
			{
				Log.WriteLog("Failed to make client config");
				return "{\"error\":\"client config failed\"}";
			}

			return toClient.ToString();
		}

		protected bool IsAdmin()
		{
            if (Request.QueryString["secret"] == config.GetConfigString("secret", "SupervisorHardcodedPassword"))
				return true;
			return false;
		}

		protected string GetFreeDiskSpace(string drive = "dataDrive")
		{
			string space = "{\"space\":-1}";

			try
			{
				DriveInfo di = new DriveInfo(config.GetConfigSourceOption(drive));
				space = string.Format("{{\"space\":{0}}}", (int)((di.AvailableFreeSpace / (double)di.TotalSize) * 100.0 + 0.5));
			}
			catch
			{
				Log.WriteLog("Failed to check disk space");
			}

			return space;
		}
	}

	public class IDDesc
	{
		protected const string dir = "id";
		private static object locker = new Object();

		public string id = "";
		public string desc = "";

		public static Hashtable Load(string path)
		{
			Hashtable ht = new Hashtable();
			try
			{
				string[] files = Directory.GetFiles(path + dir);
				foreach (string file in files)
				{
					lock (locker)
					{
						try
						{
							string fname = Path.GetFileName(file);
							IDDesc iddesc = new IDDesc();
							//iddesc.id = Encoding.ASCII.GetString(Convert.FromBase64String(fname));
							iddesc.id = fname;
							iddesc.desc = File.ReadAllText(path + dir + "\\" + fname);
							ht.Add(iddesc.id, iddesc.desc);
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
				Log.WriteLog("Failed to load id descriptions");
			}
			return ht;
		}

		public bool Save(string path)
		{
			bool ret = true;
			lock (locker)
			{
				try
				{
					if (!Directory.Exists(path + dir))
						Directory.CreateDirectory(path + dir);
					//string fname = Convert.ToBase64String(Encoding.ASCII.GetBytes(id), Base64FormattingOptions.None);
					string fname = id;
					File.WriteAllText(path + dir + "\\" + fname, desc);
				}
				catch
				{
					ret = false;
					Log.WriteLog("Failed to save id description");
				}
			}
			return ret;
		}
	}

	public class Config
	{
		public JObject config = null;
		public string source = "";

		public string GetConfigSourceOption(string key, string defVal = "")
		{
			if (config[source + "_server"] != null && config[source + "_server"][key] != null)
				return (string)config[source + "_server"][key];
			return defVal;
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

	public class DICOMFolder
	{
		public string id = "";
		public string path = "";
	}

	public class DICOMFile : DICOMFolder
	{
		public string fname = "";
	}

	public class AsyncFileData
	{
		public Semaphore semaphore = null;
		public string url = "";
		public string path = "";
		public string fname = "";
		public int timeout = 300000;
	}

	public class AsyncInstanceData
	{
		public Semaphore semaphore = null;
		public string url = null;
		public DICOMFile item = null;
	}

	public class DownloadTools
	{
		public const string tempFolderName = "temp";

		public static string UniqueFileName(string path)
		{
			string suffix = "";
			string fname = "";
			string dt = DateTime.Now.ToString("dd-MM-yyyy-HH-mm-ss-fffffff");
			int i = 0;
			do
			{
				fname = dt + suffix + ".zip";
				suffix = "(" + (++i).ToString() + ")";
			}
			while (File.Exists(path + fname));

			return fname;
		}

		public static string UniqueFolderName(string path)
		{
			string suffix = "";
			string dir = "";
			string dt = DateTime.Now.ToString("dd-MM-yyyy-HH-mm-ss-fffffff");
			int i = 0;
			do
			{
				dir = dt + suffix;
				suffix = "(" + (++i).ToString() + ")";
			}
			while (Directory.Exists(path + dir));

			return dir;
		}

		public static string GetJSON(string host, string type, string id)
		{
			string res = "";

			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(host + type + "/" + id);
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

		public static void DownloadFile(string host, string id, string path)
		{
			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(host + "instances/" + id + "/file");
			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using (Stream output = File.OpenWrite(path))
			using (Stream input = response.GetResponseStream())
			{
				input.CopyTo(output);
			}
			response.Close();
		}

		public static string GetSafeFilename(string filename)
		{
			return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
		}

		public static string GetUniqueName(string path, string fname, bool isFile)
		{
			fname = GetSafeFilename(fname);

			string res = "";

			string suffix = "";
			int i = 0;
			do
			{
				res = fname + suffix + (isFile ? ".dcm" : "");
				suffix = "(" + (++i).ToString() + ")";
			}
			while (isFile ? File.Exists(path + res) : Directory.Exists(path + res));

			return res;
		}

		public static void DownloadThread(object obj)
		{
			AsyncFileData afdata = obj as AsyncFileData;
			afdata.semaphore.WaitOne();

			try
			{
				WebRequest request = WebRequest.Create(afdata.url);
				request.Timeout = afdata.timeout;
				WebResponse response = request.GetResponse();
				using (Stream output = File.OpenWrite(afdata.path + GetUniqueName(afdata.path, afdata.fname, true)))
				using (Stream input = response.GetResponseStream())
				{
					input.CopyTo(output);
				}
				response.Close();
			}
			catch (Exception e)
			{
				Log.WriteLog(e.Message);
			}

			afdata.semaphore.Release();
		}

		public static void InstanceDataThread(object obj)
		{
			AsyncInstanceData aidata = obj as AsyncInstanceData;
			aidata.semaphore.WaitOne();

			try
			{
				WebRequest request = WebRequest.Create(aidata.url);
				HttpWebResponse response = (HttpWebResponse)request.GetResponse();
				using (Stream dataStream = response.GetResponseStream())
				{
					StreamReader reader = new StreamReader(dataStream);
					string res = reader.ReadToEnd();
					reader.Close();
					aidata.item.fname = string.Format("{0:D4}", (int)JObject.Parse(res)["IndexInSeries"]);
				}
				response.Close();
			}
			catch (Exception e)
			{
				Log.WriteLog(e.Message);
			}

			aidata.semaphore.Release();
		}

		public static string Download(Config config, string patientID, string studyID, bool radiant)
		{
			string host = config.GetConfigSourceOption("resthost");
			string tempPath = GetTempPath();
			string baseDir = "";
			try
			{
				//base folder
				baseDir = UniqueFolderName(tempPath);
				string baseTempPath = tempPath + baseDir + "\\";
				Directory.CreateDirectory(baseTempPath);

				//patient
				string jsonPatient = GetJSON(host, "patients", patientID);
				JObject jPatient = JObject.Parse(jsonPatient);
				DICOMFolder dfPatient = new DICOMFolder();
				dfPatient.id = patientID;
				dfPatient.path = baseTempPath + GetUniqueName(baseTempPath, (string)jPatient["MainDicomTags"]["PatientName"], false) + "\\";
				Directory.CreateDirectory(dfPatient.path);

				//study(ies)
				List<DICOMFolder> dfStudies = new List<DICOMFolder>();
				if (studyID != null && studyID.Length > 0)
				{
					DICOMFolder dfStudy = new DICOMFolder();
					dfStudy.id = studyID;
					dfStudies.Add(dfStudy);
				}
				else
				{
					foreach (JToken jStudyID in (JArray)jPatient["Studies"])
					{
						DICOMFolder dfStudy = new DICOMFolder();
						dfStudy.id = (string)jStudyID;
						dfStudies.Add(dfStudy);
					}
				}

				List<DICOMFile> dfInstances = new List<DICOMFile>();
				foreach (DICOMFolder dfStudy in dfStudies)
				{
					try
					{
						string jsonStudy = GetJSON(host, "studies", dfStudy.id);
						JObject jStudy = JObject.Parse(jsonStudy);
						dfStudy.path = dfPatient.path + GetUniqueName(dfPatient.path,
							(string)jStudy["MainDicomTags"]["StudyDate"] + " " + (string)jStudy["MainDicomTags"]["StudyTime"] + " " + (string)jStudy["MainDicomTags"]["StudyDescription"], false) + "\\";
						Directory.CreateDirectory(dfStudy.path);

						//series
						List<DICOMFolder> dfSeries = new List<DICOMFolder>();
						foreach (JToken jSeriesID in (JArray)jStudy["Series"])
						{
							try
							{
								DICOMFolder dfSer = new DICOMFolder();
								dfSer.id = (string)jSeriesID;
								dfSeries.Add(dfSer);

								string jsonSer = GetJSON(host, "series", dfSer.id);
								JObject jSer = JObject.Parse(jsonSer);
								dfSer.path = dfStudy.path + GetUniqueName(dfStudy.path,
									(string)jSer["MainDicomTags"]["SeriesNumber"] + " " + (string)jSer["MainDicomTags"]["SeriesDescription"], false) + "\\";
								Directory.CreateDirectory(dfSer.path);

								//instances
								int i = 0;
								foreach (JToken jInstanceID in (JArray)jSer["Instances"])
								{
									try
									{
										DICOMFile dfInstance = new DICOMFile();
										dfInstance.id = (string)jInstanceID;
										dfInstance.path = dfSer.path;
										dfInstance.fname = string.Format("{0:D4}", ++i);
										dfInstances.Add(dfInstance);
									}
									catch (Exception e)
									{
										Log.WriteLog("Failed to get instance's data: " + e.Message);
									}
								}
							}
							catch (Exception e)
							{
								Log.WriteLog("Failed to get series' data: " + e.Message);
							}
						}
					}
					catch (Exception e)
					{
						Log.WriteLog("Failed to get study's data: " + e.Message);
					}
				}

				//correct instance file name
				int simultaneousNum = config.GetConfigInt("simultaneousThreads", 10);
				Semaphore semaphore = new Semaphore(simultaneousNum, simultaneousNum);
				List<Thread> threads = new List<Thread>(dfInstances.Count);
				foreach (DICOMFile instance in dfInstances)
				{
					try
					{
						AsyncInstanceData aid = new AsyncInstanceData();
						aid.semaphore = semaphore;
						aid.url = host + "instances/" + instance.id;
						aid.item = instance;

						Thread thread = new Thread(InstanceDataThread);
						threads.Add(thread);
						thread.Start(aid);
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

				//instances' files
				simultaneousNum = config.GetConfigInt("simultaneousInstanceThreads", 10);
				semaphore = new Semaphore(simultaneousNum, simultaneousNum);
				threads = new List<Thread>(dfInstances.Count);
				foreach (DICOMFile instance in dfInstances)
				{
					try
					{
						AsyncFileData afd = new AsyncFileData();
						afd.semaphore = semaphore;
						afd.url = host + "instances/" + instance.id + "/file";
						afd.path = instance.path;
						afd.fname = instance.fname;
						afd.timeout = config.GetConfigInt("restTimeout", 300000);

						Thread thread = new Thread(DownloadThread);
						threads.Add(thread);
						thread.Start(afd);
					}
					catch (Exception e)
					{
						Log.WriteLog("Failed to download instances: " + e.Message);
					}
				}

				allDone = false;
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
			catch (Exception e)
			{
				Log.WriteLog("Failed to get patient's data: " + e.Message);
			}

			return Compress(baseDir, radiant, config.GetConfigInt("compressionLevel", 0));
		}

		public static string Download(string url, int timeout)
		{
			string fname = "";
			string tempPath = GetTempPath();

			try
			{
				fname = UniqueFileName(tempPath);

				HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
				request.Timeout = timeout;
				HttpWebResponse response = (HttpWebResponse)request.GetResponse();
				using (Stream output = File.OpenWrite(tempPath + fname))
				using (Stream input = response.GetResponseStream())
				{
					input.CopyTo(output);
				}
				response.Close();
			}
			catch
			{
				Log.WriteLog("Failed to download: " + tempPath + fname);
				return "";
			}

			return fname;
		}

		public static string GetTempPath()
		{
			return HttpContext.Current.Server.MapPath("~") + tempFolderName + "\\";
		}

		public static string GetTempUrl()
		{
			return tempFolderName + "/";
		}

		public static string Compress(string dir, bool radiant = true, int comprLvl = 0)
		{
			string path = GetTempPath();

			if (!Directory.Exists(path + dir))
				return "";

			try
			{
				if (radiant)
					DirectoryCopy(HttpContext.Current.Server.MapPath("~") + "radiant", path + dir);
			}
			catch
			{
				Log.WriteLog("Failed to copy radiant CD/DVD viewer");
			}

			string fname = UniqueFileName(path + dir);
			try
			{
				CompressionLevel lvl = CompressionLevel.NoCompression;
				if (comprLvl == 1)
					lvl = CompressionLevel.Fastest;
				else if (comprLvl > 1)
					lvl = CompressionLevel.Optimal;
				ZipFile.CreateFromDirectory(path + dir, path + fname, lvl, false);
			}
			catch (Exception e)
			{
				Log.WriteLog(e.Message);
				return "";
			}

			try
			{
				Directory.Delete(path + dir, true);
				File.SetCreationTime(path + fname, DateTime.Now);
			}
			catch
			{
			}

			return fname;
		}

		public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs = true)
		{
			DirectoryInfo dir = new DirectoryInfo(sourceDirName);
			if (!dir.Exists)
				throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);

			DirectoryInfo[] dirs = dir.GetDirectories();
			if (!Directory.Exists(destDirName))
				Directory.CreateDirectory(destDirName);

			FileInfo[] files = dir.GetFiles();
			foreach (FileInfo file in files)
			{
				string temppath = Path.Combine(destDirName, file.Name);
				file.CopyTo(temppath, true);
			}

			if (copySubDirs)
			{
				foreach (DirectoryInfo subdir in dirs)
				{
					string temppath = Path.Combine(destDirName, subdir.Name);
					DirectoryCopy(subdir.FullName, temppath, copySubDirs);
				}
			}
		}

        public static void ClearTempFolder(int lifeTime)
        {
            try
            {
                string path = GetTempPath();
                if (!Directory.Exists(path))
                    return;

                DateTime curDT = DateTime.Now;

                DirectoryInfo dir = new DirectoryInfo(path);

                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo f in files)
                {
                    TimeSpan ts = curDT - f.CreationTime;
                    if (ts.TotalMinutes > lifeTime)
                    {
                        try
                        {
                            File.Delete(f.FullName);
                        }
                        catch
                        {
                            Log.WriteLog("Failed to delete file " + f.FullName);
                        }
                    }
                }

                DirectoryInfo[] dirs = dir.GetDirectories();
                foreach (DirectoryInfo d in dirs)
                {
                    TimeSpan ts = curDT - d.CreationTime;
                    if (ts.TotalMinutes > lifeTime)
                    {
                        try
                        {
                            Directory.Delete(d.FullName, true);
                        }
                        catch
                        {
                            Log.WriteLog("Failed to delete directory " + d.FullName);
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}