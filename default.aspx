<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="default.aspx.cs" Inherits="MiddleEarth.MiddleEarthMainForm" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Palantir</title>

	<link rel="icon" href="/images/palantir_small.png" type="image/png"/>
	<link rel="shortcut icon" href="/images/palantir_small.png" type="image/png"/>

	<meta http-equiv='cache-control' content='no-cache' />
	<meta http-equiv='expires' content='0' />
	<meta http-equiv='pragma' content='no-cache' />

	<script type="text/javascript" src="jquery/jquery.min.js"></script>
	<script type="text/javascript" src="jquery/jquery-ui.min.js"></script>
	<script type="text/javascript" src="moment/moment.min.js"></script>
	<script type="text/javascript" src="cookies/js.cookie.js"></script>
	<script type="text/javascript" src="alertify/alertify.min.js"></script>

	<link rel="stylesheet" href="alertify/alertify.core.css" />
	<link rel="stylesheet" href="alertify/alertify.default.css" />
	<link rel="stylesheet" href="jquery/jquery-ui.min.css" />
	<style>
		body, input, textarea, select
		{
			font-family: Tahoma;
			font-size: 14px;
		}

		form
		{
			padding: 0;
			margin: 0;
		}
		
		.arrangeTable
		{
			border: none;
		}

		.arrangeTable tr td
		{
			border: none;
			padding: 0;
			border-collapse: collapse;
		}

		table
		{
			border-collapse: collapse;
		}

		table tr td, table tr th
		{
			border: 1px solid gainsboro;
			padding: 4px;
			text-align: left;
		}

		table tr th
		{
            cursor: pointer;
			background-color: #eeeeee;
		}

		.tableArrange tr td
		{
			border: 0;
			padding: 0 2px;
			text-align: left;
		}

        a
        {
            text-decoration: none;
        }

		a.linkFolder
		{
			background-color: #eeeeee;
			color: green;
			padding: 1px 3px;
		}

		a.linkBurn
		{
			background-color: #eeeeee;
			color: #aa0000;
			padding: 1px 3px;
		}

		a.linkAdvanced
		{
			background-color: #eeeeee;
			color: #aa00aa;
			padding: 1px 3px;
		}

        a:hover
        {
            text-decoration: underline;
        }

		.vr
		{
			color: silver;
		}

		.labelFilter
		{
			font-size: 12px;
		}
	</style>

	<script type="text/javascript">
		var shell = null;
		try
		{
			shell = new ActiveXObject("WScript.Shell");
		}
		catch (err)
		{
		}
		var json = null;
		var config = null;
		var source = "Isengard";
		var download = false;
		alertify.set({ delay: 8000 });

		function Query (request, done, data)
		{
			if (data == null)
				data = {};
			data.source = source;
			data.secret = Get('secret');
			var period = $.find("#period")[0];
			data.period = period.options[period.selectedIndex].value;

			return $.ajax({
				url: request,
				type: 'GET',
				timeout: 300000,
				data: data,
				dataType: 'json',
				cache: false,
				success: function (d, textStatus, jqXHR)
				{
					if (!d.error)
						done(true, d, textStatus, jqXHR);
					else
						done(false, d, textStatus, jqXHR);
				},
				error: function (d, textStatus, jqXHR)
				{
					done(false, d, textStatus, jqXHR);
				}
			});
		};

		function GetFreeDiskSpace(backup)
		{
			Query("default.aspx?request=" + (backup ? "backupSpace" : "space"), function (success, data) {
				var div = document.getElementById(backup ? "backupSpace" : "diskSpace");

				if (!success || !data.space || data.space < 0)
				{
					div.style.display = "none";
					return;
				}

				div.style.display = "block";
				var text = data.space > 33 ? "Keep calm" : "Call sysadmin!";
				var color = "#ffffff";
				text = " " + data.space + "% free. " + text;
				var w = 200;
				var h = 20;
				div.style.height = "" + h + "px";
				div.style.width = "" + w + "px";
				div.style.backgroundColor = "#ee0000";
				div.style.color = color;

				var progress = document.getElementById(backup ? "backupSpaceProgress" : "diskSpaceProgress");
				progress.style.backgroundColor = "green";
				progress.style.color = color;
				progress.innerText = text;
				progress.style.width = "" + (data.space * w / 100) + "px";
				progress.style.height = "" + h + "px";
			});
		}

		function CanViewInRadiant()
		{
			return (shell ? true : false);
		}

		var escapeMap = {
			'&': '&amp;',
			'<': '&lt;',
			'>': '&gt;',
			'"': '&quot;',
			"'": '&#039;'
		};
		function escapeHtml(text)
		{
			return text.replace(/[&<>"']/g, function (m) { return escapeMap[m]; });
		}

		function Delete(obj, type)
		{
			var info = (type == "patient" ? '<div>Patient: <b>' + escapeHtml(obj.patient.MainDicomTags.PatientName + ' - ' + obj.patient.MainDicomTags.PatientID) + '</b></div>' :
				'<div>Study: <b>' + GetStudyDisplayName(obj.study) + '</b></div>' + '<div>(of patient: ' + escapeHtml(obj.patient.MainDicomTags.PatientName + ' - ' + obj.patient.MainDicomTags.PatientID) + ')</div>');

			var html = info + '<div style="height: 8px;"></div><div><input type="checkbox" id="sure" /><label for="sure"><b>Yes, I&#039;m sure</b></label></div>';

			$("<div></div>").dialog({
				buttons:
				[
					{
						text: "Ok",
						click: function ()
						{
							if ($(this).find("#sure")[0].checked)
							{
								alertify.log("Job started (delete)");
								Query("default.aspx?request=delete",
									function (success, data)
									{
										if (!success)
										{
											alertify.error("Failed to delete");
											return;
										}

										alertify.success("Job done (deleted)");
										LoadList();
									},
									{
										'id': (type == "study" ? obj.study.ID : obj.patient.ID),
										'patient': (type == "study" ? 'false' : 'true')
									});
							}
							$(this).dialog("close");
						}
					},
					{
						text: "Cancel",
						click: function ()
						{
							$(this).dialog("close");
						}
					}
				],
				modal: true,
				title: "Selected record will be deleted permanently. Are you sure?",
				width: 600
			}).html(html);
		}

		function IsAdmin()
		{
			return (config.admin === 'true' ? true : false);
		}

		function RadioChange(obj)
		{
			$(obj.parentElement.parentElement).find("#checkboxes")[0].style.display = $(obj.parentElement.parentElement).find("#download")[0].checked ? "block" : "none";
			if (IsAdmin())
				$(obj.parentElement.parentElement).find("#checkboxAdv")[0].style.display = $(obj.parentElement.parentElement).find("#advanced")[0].checked ? "block" : "none";
		}

		var descVal = "";
		function DescChanged(obj)
		{
			if (!IsAdmin())
				return;

			if (obj.value != descVal)
			{
				descVal = obj.value;
				$(obj.parentElement.parentElement).find("#edit")[0].checked = true;
				RadioChange(obj);
			}
		}

		function UpdateListDescription(id, desc)
		{
			for (var i = 0; i < json.length; i++)
			{
				if (json[i].patient.ID === id)
					json[i].patient.iddesc = desc;
			}
			$("a[name=descLink]").each(function () {
				if (this.object.patient.ID === id)
					this.innerText = desc;
			});
		}

		function Click(e)
		{
			e = e || window.event;
			var target = e.target || e.srcElement;
			if (target.tagName != 'A')
				target = target.parentElement;
			e.cancelBubble = true;
			if (e.stopPropagation)
				e.stopPropagation();
			if (e.preventDefault)
				e.preventDefault();

			descVal = target.object.patient.iddesc;

			var info = (target.objectType == "patient" ? '<div>Patient: <b>' + escapeHtml(target.object.patient.MainDicomTags.PatientName + ' - ' + target.object.patient.MainDicomTags.PatientID) + '</b></div>' :
				'<div>Study: <b>' + GetStudyDisplayName(target.object.study) + '</b></div>' +
				'<div>(of patient: ' + escapeHtml(target.object.patient.MainDicomTags.PatientName + ' - ' + target.object.patient.MainDicomTags.PatientID) + ')</div>');

			var advanced = (IsAdmin() ? '<div><input type="radio" id="advanced" name="click" onchange="RadioChange(this)" onclick="this.blur();" /><label for="advanced">Advanced</label></div> \
<div id="checkboxAdv" style="display: none; margin-left: 16px;"> \
<input type="checkbox" id="delete" /><label for="delete">Delete</label><br> \
</div>'	: '');

			var html = info + '<div style="height: 4px;"></div> \
<div><input type="radio" id="edit" name="click" onchange="RadioChange(this)" onclick="this.blur();"' + (IsAdmin() ? '' : ' disabled') + ' /><label for="edit"' + (IsAdmin() ? '' : ' disabled') + '>Edit</label></div> \
<div style="margin-left: 20px;"> \
<label for="description">Description: </label><input type="text" id="description" style="width: 250px;" onkeyup="DescChanged(this)" value="' +
(target.object.patient.iddesc ? escapeHtml(target.object.patient.iddesc) : '') + '"' + (IsAdmin() ? '' : ' readonly') + ' /><br></div> \
<div><input type="radio" id="view" name="click" onchange="RadioChange(this)" onclick="this.blur();" /><label for="view">View</label></div> \
<div><input type="radio" id="download" name="click" onchange="RadioChange(this)" onclick="this.blur();" /><label for="download">Download</label></div> \
<div id="checkboxes" style="display: none; margin-left: 16px;"> \
<input type="checkbox" id="radiant" /><label for="radiant">RadiAnt CD/DVD Viewer</label><br> \
</div>' + advanced;
			$("<div></div>").dialog({
				buttons:
				[
					{
						text: "Ok",
						click: function ()
						{
							if ($(this).find("#edit")[0].checked)
							{
								var desc = $(this).find("#description")[0].value;
								Query("default.aspx?request=savedesc",
									function (success, data)
									{
										if (!success)
										{
											alertify.error("Failed to save");
											return;
										}
										UpdateListDescription(target.object.patient.ID, desc);

										alertify.success("Saved");
									},
									{
										'id': target.object.patient.ID,
										'desc': desc
									});
							}
							else if ($(this).find("#view")[0].checked)
							{
								if (CanViewInRadiant())
								{
									var path = '"' + Get('viewer') + '"';
									var exe = (target.objectType == "patient" ?
										path + ' -paet ' + GetConfigOption("aet") + ' -pstv 00100020 "' + target.object.patient.MainDicomTags.PatientID + '"' :
										path + ' -paet ' + GetConfigOption("aet") + ' -pstv 0020000D "' + target.object.study.MainDicomTags.StudyInstanceUID + '"');
									shell.Run(exe);
								}
								else
								{
									if (target.objectType == "patient")
									{
										target.object.patient.Studies.forEach(function (e)
										{
											window.open(GetConfigOption("webviewer") + "osimis-viewer/app/index.html?study=" + e, "_blank");
										});
									}
									else
									{
										window.open(GetConfigOption("webviewer") + "osimis-viewer/app/index.html?study=" + target.object.study.ID, "_blank");
									}
								}
							}
							else if ($(this).find("#download")[0].checked)
							{
								if (download)
								{
									alertify.log("Download currently is in progress. Please wait for job completion", "notify");
								}
								else
								{
									download = true;
									alertify.log("Job started (download). Wait until server proceeds with files", "notify");
									Query("default.aspx?request=download",
										function (success, data)
										{
											download = false;
											if (!success)
											{
												alertify.error("Server is failed to process download request");
												return;
											}

											alertify.success("Job done (ready for download)");
											window.location = data.path;
										},
										{
											'patient': (target.objectType == "study" ? target.object.study.ParentPatient : target.object.patient.ID),
											'study': (target.objectType == "study" ? target.object.study.ID : ""),
											'radiant' : ($(this).find("#radiant")[0].checked ? 'true' : 'false')
										});
								}
							}
							else if (IsAdmin() && $(this).find("#advanced")[0].checked)
							{
								if ($(this).find("#delete")[0].checked)
								{
									$(this).dialog("close");
									Delete(target.object, target.objectType);
									return;
								}
							}
							$(this).dialog("close");
						}
					},
					{
						text: "Cancel",
						click: function ()
						{
							$(this).dialog("close");
						}
					}
				],
				modal: true,
				title: "Action",
				width: 600
			}).html(html);

			return false;
		}

		function LoadList()
		{
			Query("default.aspx?request=list", function (success, data)
			{
				var table = document.getElementById("listTable");

				ClearList(table);

				if (!success)
				{
					alertify.error("Failed to load list");
					return;
				}

				json = data;

				if (!json)
					return;

				Sort();
				FillList(table);
			});

			GetFreeDiskSpace();
			GetFreeDiskSpace(true);
		}

		function ClearList(table)
		{
			table = table.tBodies[0];
			while (table.firstChild)
			{
				table.removeChild(table.firstChild);
			}
		}

		function FillList(table)
		{
            table = table.tBodies[0];
            var j = 0;
			for (var i = 0; i < json.length; i++)
			{
				if (!Filter(json[i]))
					continue;

				var tr = document.createElement('tr');
				table.appendChild(tr);

				var tdN = document.createElement('td');
				tdN.innerHTML = "" + (++j);
				tr.appendChild(tdN);

				var tdStudy = document.createElement('td');
				tr.appendChild(tdStudy);

				var aStudy = document.createElement('a');
				aStudy.innerHTML = GetStudyDisplayName(json[i].study);
				aStudy.href = "#";
				aStudy.object = json[i];
				aStudy.objectType = "study";
				aStudy.target = "_blank";
				aStudy.onclick = Click;
				tdStudy.appendChild(aStudy);

				var tdPatient = document.createElement('td');
				tr.appendChild(tdPatient);

				var aPatient = document.createElement('a');
				aPatient.innerHTML = escapeHtml(json[i].patient.MainDicomTags.PatientName);
				aPatient.href = "#";
				aPatient.object = json[i];
				aPatient.objectType = "patient";
				aPatient.target = "_blank";
				aPatient.onclick = Click;
				tdPatient.appendChild(aPatient);

				var tdID = document.createElement('td');
				tr.appendChild(tdID);

				var aID = document.createElement('a');
				aID.innerHTML = escapeHtml(json[i].patient.MainDicomTags.PatientID);
				aID.href = "#";
				aID.object = json[i];
				aID.objectType = "patient";
				aID.target = "_blank";
				aID.onclick = Click;
				tdID.appendChild(aID);

				var tdDesc = document.createElement('td');
				tr.appendChild(tdDesc);

				var aDesc = document.createElement('a');
				aDesc.innerHTML = json[i].patient.iddesc ? escapeHtml(json[i].patient.iddesc) : '';
				aDesc.href = "#";
				aDesc.name = "descLink";
				aDesc.object = json[i];
				aDesc.objectType = "patient";
				aDesc.target = "_blank";
				aDesc.onclick = Click;
				tdDesc.appendChild(aDesc);
			}
		}

		function SourceChanged()
		{
			var sselect = $.find("#source")[0];
			source = sselect.options[sselect.selectedIndex].text;

			LoadList();
		}

		function Load()
		{
			var period = document.getElementById('period');
			period.value = Cookies.get("palantirPeriod") || "7";
			//for IE < 9
			for (var i = 0; i < period.options.length; i++)
			{
				if (period.value == period.options[i].value)
				{
					period.selectedIndex = i;
					break;
				}
			}

			$(document.getElementById('minDate')).datepicker({ dateFormat: 'dd.mm.yy', altField: '#minDateAlt', altFormat: 'yymmdd', changeMonth: true, changeYear: true, yearRange: 2000 + ':' + moment().year() });
			$(document.getElementById('maxDate')).datepicker({ dateFormat: 'dd.mm.yy', altField: '#maxDateAlt', altFormat: 'yymmdd', changeMonth: true, changeYear: true, yearRange: 2000 + ':' + moment().year() });

			Query("default.aspx?request=config", function (success, data)
			{
				if (!success)
				{
					alertify.log("Failed to load config");
					return;
				}

				config = data;
				var sselect = $.find("#source")[0];
				for (var i = 0; i < config.sources.length; i++)
				{
					var option = document.createElement('OPTION');
					option.innerText = config.sources[i];
					sselect.appendChild(option);
				}
				sselect.selectedIndex = 0;
				//sselect.focus();
				//sselect.blur();
				source = sselect.options[0].text;

				LoadList();
			});
		}

		function GetConfigOption(key)
		{
			return config[source][key];
		}

		function MessageBox(msg)
		{
			var html = msg;
			$("<div></div>").dialog({
				buttons:
				[
					{
						text: "Ok",
						click: function ()
						{
							$(this).dialog("close");
						}
					}
				],
				modal: true,
				title: "Message"
			}).html(html);
		}

		function ViewerBrowseClick(obj)
		{
			$(obj.parentElement).find("#viewer")[0].click();
		}

		function OnViewerChange(obj)
		{
			$(obj.parentElement).find("#viewerText")[0].value = obj.value;
		}

		function Get(key)
		{
			return Cookies.get(key);
		}

		var sortColumns = ["study", "patient", "id", "desc"];
		var sortOrder = { "study": "desc", "patient": "asc", "id": "asc", "desc": "asc" };
		function Sort()
		{
			if (!json)
				return;

			json.sort(function (a, b)
			{
				var res = subsort(sortColumns[0], a, b);
				for (var i = 1; i < sortColumns.length; i++)
				{
					if (res == 0)
						res = subsort(sortColumns[i], a, b);
				}
				return res;
			});
		}

		function SetSortColumn(col)
		{
			for (var i = 0; i < sortColumns.length; i++)
			{
				if (col == sortColumns[i])
				{
					sortColumns.splice(i, 1);
					sortColumns.unshift(col);
					break;
				}
			}
		}

		function subsort(col, a, b)
		{
			var res = 0;
			if (col == "study")
			{
				res = Compare(a.study.MainDicomTags.StudyDate, b.study.MainDicomTags.StudyDate);
				if (res == 0)
					res = Compare(a.study.MainDicomTags.StudyTime, b.study.MainDicomTags.StudyTime);
				if (res == 0)
					res = Compare(a.study.MainDicomTags.StudyDescription, b.study.MainDicomTags.StudyDescription);
			}
			else if (col == "patient")
			{
				res = Compare(a.patient.MainDicomTags.PatientName, b.patient.MainDicomTags.PatientName);
			}
			else if (col == "id")
			{
				res = Compare(a.patient.MainDicomTags.PatientID, b.patient.MainDicomTags.PatientID);
			}
			else if (col == "desc")
			{
				res = Compare(a.patient.iddesc, b.patient.iddesc);
			}
			if (res < 0)
				return (sortOrder[col] == "asc" ? -1 : 1);
			if (res > 0)
				return (sortOrder[col] == "asc" ? 1 : -1);
			return 0;
		}

		function Compare(a, b)
		{
			a = a || '';
			b = b || '';
			if (a < b)
				return -1;
			if (a > b)
				return 1;
			return 0;
		}

		function SortClick(e)
		{
			e = e || window.event;
			var src = e.target || e.srcElement;

			if (src.id == "thStudy")
			{
				SetSortColumn("study");
			}
			else if (src.id == "thPatient")
			{
				SetSortColumn("patient");
			}
			else if (src.id == "thID")
			{
				SetSortColumn("id");
			}
			else if (src.id == "thDesc")
			{
				SetSortColumn("desc");
			}
			sortOrder[sortColumns[0]] = sortOrder[sortColumns[0]] == "asc" ? "desc" : "asc";

			Sort();
			var table = document.getElementById("listTable");
			ClearList(table);
			FillList(table);
		}

		function FormatDateTime(study)
		{
			var res = "";
			var d = study.MainDicomTags.StudyDate;
			var t = study.MainDicomTags.StudyTime;

			if (d.length >= 4)
				res = d.substr(0, 4);
			if (d.length >= 6)
				res = d.substr(4, 2) + '.' + res;
			if (d.length >= 8)
				res = d.substr(6, 2) + '.' + res;

			if (t)
			{
				if (t.length >= 2)
					res += " " + t.substr(0, 2);
				if (t.length >= 4)
					res += ":" + t.substr(2, 2);
				if (t.length >= 6)
					res += ":" + t.substr(4, 2);
			}

			return res;
		}

		function GetStudyDisplayName(study)
		{
			return FormatDateTime(study) + ' - <span style="color: #555;">' + escapeHtml(study.MainDicomTags.StudyDescription) + '</span>';
		}

		if (typeof String.prototype.trim !== 'function')
		{
			String.prototype.trim = function ()
			{
				return this.replace(/^\s+|\s+$/g, '');
			}
		}

		function Filter(obj)
		{
			var minDate = document.getElementById('minDateAlt').value.trim();
			if (minDate.length < 8)
				minDate = null;
			if (minDate && obj.study.MainDicomTags.StudyDate < minDate)
				return false;

			var maxDate = document.getElementById('maxDateAlt').value.trim();
			if (maxDate.length < 8)
				maxDate = null;
			if (maxDate && obj.study.MainDicomTags.StudyDate > maxDate)
				return false;

			var name = document.getElementById('name').value.trim().toUpperCase();
			if (name.length == 0)
				name = null;
			if (name && obj.patient.MainDicomTags.PatientName.trim().toUpperCase().indexOf(name) < 0 && (!obj.patient.iddesc || obj.patient.iddesc.trim().toUpperCase().indexOf(name) < 0))
				return false;

			var id = document.getElementById('id').value.trim().toUpperCase();
			if (id.length == 0)
				id = null;
			if (id && obj.patient.MainDicomTags.PatientID.trim().toUpperCase() != id)
				return false;

			return true;
		}

		function IsDateValid(date)
		{
			return date && date.length > 0 && moment(date, "D.M.YYYY", true).isValid();
		}

		function ApplyFilter()
		{
			var minDate = document.getElementById('minDate').value.trim();
			if (!IsDateValid(minDate))
				document.getElementById('minDateAlt').value = '';

			var maxDate = document.getElementById('maxDate').value.trim();
			if (!IsDateValid(maxDate))
				document.getElementById('maxDateAlt').value = '';

			var table = document.getElementById("listTable");
			ClearList(table);
			FillList(table);
		}

		function ResetFilter()
		{
			document.getElementById('minDate').value = '';
			document.getElementById('minDateAlt').value = '';
			document.getElementById('maxDate').value = '';
			document.getElementById('maxDateAlt').value = '';
			document.getElementById('name').value = '';
			document.getElementById('id').value = '';

			ApplyFilter();
		}

		function PeriodChanged()
		{
			var period = $.find("#period")[0];
			Cookies.set("palantirPeriod", period.options[period.selectedIndex].value, { expires: 10000 });

			LoadList();
		}
	</script>
</head>
<body onload="Load()" style="position: relative;">
    <form id="middleEarthMainForm" runat="server" onsubmit="return false">
		<table class="arrangeTable">
			<tr>
				<td style="display: none;">
					<label class="labelFilter">Source:</label><br />
					<select id="source" onchange="SourceChanged()" title="Source">
					</select>
				</td>
				<td>
					<label class="labelFilter">Period:</label><br />
					<select id="period" onchange="PeriodChanged()" title="Period">
						<option value="1">Today</option>
						<option value="7" selected="selected">7 days</option>
						<option value="30">30 days</option>
						<option value="90">90 days</option>
						<option value="180">180 days</option>
						<option value="365">365 days</option>
						<option value="0">All</option>
					</select>
				</td>
				<td style="padding-left: 4px;">
					<br />
					<input type="image" src="/images/reload.png" style="width: 16px;" title="Reload" onclick="LoadList()" />
				</td>
				<td style="padding-left: 10px;">
					<label class="labelFilter">Min date:</label><br />
					<input type="text" id="minDate" title="Min date" style="width: 80px;" />
					<input type="hidden" id="minDateAlt" />
				</td>
				<td style="padding-left: 4px;">
					<label class="labelFilter">Max date:</label><br />
					<input type="text" id="maxDate" title="Max date" style="width: 80px;" />
					<input type="hidden" id="maxDateAlt" />
				</td>
				<td style="padding-left: 4px;">
					<label class="labelFilter">Patient's name:</label><br />
					<input type="text" id="name" title = "Patient's name" style="width: 140px;" />
				</td>
				<td style="padding-left: 4px;">
					<label class="labelFilter">ID:</label><br />
					<input type="text" id="id" title = "ID" style="width: 80px;" />
				</td>
				<td style="padding-left: 4px;">
					<br />
					<input type="image" src="/images/apply.png" style="width: 16px;" title="Apply filter" onclick="ApplyFilter()" /> 
				</td>
				<td style="padding-left: 4px;">
					<br />
					<input type="image" src="/images/reset.png" style="width: 16px;" title="Reset filter" onclick="ResetFilter()" />
				</td>
				<td style="padding-left: 20px;">
					<label class="labelFilter">Disk space:</label><br />
					<div id="diskSpace" title="Disk space" style="border: 1px solid black; display: none;">
						<div id="diskSpaceProgress" style="overflow-x: visible; white-space: nowrap;"></div>
					</div>
				</td>
				<td style="padding-left: 20px;">
					<label class="labelFilter">Backup space:</label><br />
					<div id="backupSpace" title="Backup space" style="border: 1px solid black; display: none;">
						<div id="backupSpaceProgress" style="overflow-x: visible; white-space: nowrap;"></div>
					</div>
				</td>
			</tr>
		</table>
		<div style="height: 4px;"></div>
        <div>
			<table id="listTable">
				<thead>
					<tr>
						<th>#</th>
						<th id="thStudy" onclick="SortClick(event)">Study</th>
						<th id="thPatient" onclick="SortClick(event)">Patient</th>
						<th id="thID" onclick="SortClick(event)">ID</th>
						<th id="thDesc" onclick="SortClick(event)">Description</th>
					</tr>
				</thead>
				<tbody>
				</tbody>
			</table>
        </div>
    </form>
</body>
</html>
