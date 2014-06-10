﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Media;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using ThinkingHome.Core.Plugins;
using ThinkingHome.Core.Plugins.Utils;
using ThinkingHome.Plugins.AlarmClock.Data;
using ThinkingHome.Plugins.Listener;
using ThinkingHome.Plugins.Listener.Api;
using ThinkingHome.Plugins.Scripts;
using ThinkingHome.Plugins.Scripts.Data;
using ThinkingHome.Plugins.Timer;

namespace ThinkingHome.Plugins.AlarmClock
{
	[Plugin]
	public class AlarmClockPlugin : Plugin
	{
		private readonly object lockObject = new object();
		private DateTime lastAlarmTime = DateTime.MinValue;
		private List<AlarmTime> times;

		private SoundPlayer player;

		public override void InitDbModel(ModelMapper mapper)
		{
			mapper.Class<AlarmTime>(cfg => cfg.Table("AlarmClock_AlarmTime"));
		}

		public override void Start()
		{
			player = new SoundPlayer(SoundResources.Ring02);
		}

		public override void Stop()
		{
			player.Dispose();
		}

		#region public

		public void ReloadTimes()
		{
			lock (lockObject)
			{
				times = null;
				LoadTimes();
			}
		}

		public void StopAlarm()
		{
			Logger.Info("Stop all sounds");
			player.Stop();
		}

		#endregion

		#region events

		[ImportMany("0917789F-A980-4224-B43F-A820DEE093C8")]
		private Action<Guid>[] AlarmStartedForPlugins { get; set; }

		[ScriptEvent("alarmClock.alarmStarted")]
		private ScriptEventHandlerDelegate[] AlarmStartedForScripts { get; set; }

		#endregion

		#region private

		[OnTimerElapsed]
		private void OnTimerElapsed(DateTime now)
		{
			lock (lockObject)
			{
				LoadTimes();

				var alarms = times.Where(x => CheckTime(x, now, lastAlarmTime)).ToArray();

				if (alarms.Any())
				{
					lastAlarmTime = now;
					Alarm(alarms);
				}
			}
		}

		private void LoadTimes()
		{
			if (times == null)
			{
				using (var session = Context.OpenSession())
				{
					times = session.Query<AlarmTime>()
						.Fetch(a => a.UserScript)
						.Where(t => t.Enabled)
						.ToList();

					Logger.Info("loaded {0} alarm times", times.Count);
				}
			}
		}

		private static bool CheckTime(AlarmTime time, DateTime now, DateTime lastAlarm)
		{
			// если прошло время звонка будильника
			// и от этого времени не прошло 5 минут
			// и будильник сегодня еще не звонил
			var date = now.Date.AddHours(time.Hours).AddMinutes(time.Minutes);

			if (date < lastAlarm)
			{
				date = date.AddDays(1);
			}

			return now > date && now < date.AddMinutes(5) && lastAlarm < date;
		}

		private void Alarm(AlarmTime[] alarms)
		{
			Logger.Info("ALARM!");

			if (alarms.Any(a => a.PlaySound))
			{
				Logger.Info("Play sound");
				player.PlayLooping();
			}

			foreach (var alarm in alarms)
			{
				Logger.Info("Run event handlers: {0} ({1})", alarm.Name, alarm.Id);

				Guid alarmId = alarm.Id;
				Run(AlarmStartedForPlugins, x => x(alarmId));

				if (alarm.UserScript != null)
				{
					Logger.Info("Run script: {0} ({1})", alarm.UserScript.Name, alarm.UserScript.Id);
					Context.GetPlugin<ScriptsPlugin>().ExecuteScript(alarm.UserScript);
				}
			}

			Logger.Info("Run subscribed scripts");
			this.RaiseScriptEvent(x => x.AlarmStartedForScripts);
		}

		#endregion
	}
}