/*
 * Messages players about their time online in the last 24 hours (give or take 30 seconds)
 * Special thanks to Panda and Venk for hosting their plugins in public repos for people to analyze
 */

/* FOR CODERS: Quick note on datapoints in TMTable. A single datapoint is a series of login and logout events. It will look something like:
* """
* Player1+ +8/23/2021_6:34:56_PM,-8/23/2021_6:35:04_PM,+8/23/2021_6:37:47_PM,-8/23/2021_6:37:54_PM
* Player2+ +5/21/2021_2:34:56_PM,-5/21/2021_2:36:14_PM,+5/21/2021_2:38:21_PM
* """
* The playerTMSettings is a similar text file that tells us which setting to use, and which players to remind. It looks like
* """
* Player1+ daily
* Player2+ current
* If Player3 has no TM settings he will not be found in this list (thereby keeping it small)
* """
*/

using System;
using System.Collections.Generic;
using MCGalaxy.Tasks;
using MCGalaxy.Events.PlayerEvents;

namespace MCGalaxy
{
    public class TimeManagement : Plugin
    {
        public override string name { get { return "TimeManagement"; } }
        public override string creator { get { return "Opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.3.4"; } }

        public static PlayerExtList TMTable;
        public static PlayerExtList playerTMSettings;

        SchedulerTask task;
        const int HEARTBEAT_TM = 60;        // Checks every 60 seconds
        const int HOUR_IN_SECONDS = 3600;
        const int DAY_IN_SECONDS = 86400;

        public override void Load(bool startup)
        {
            Command.Register(new CmdTimeManagement());
            TMTable = PlayerExtList.Load("extra/timeManagementTable");
            playerTMSettings = PlayerExtList.Load("extra/playerTMSettings");
            task = Server.MainScheduler.QueueRepeat(TMCallback, null, TimeSpan.FromSeconds(HEARTBEAT_TM));
            OnPlayerConnectEvent.Register(HandlePlayerConnect, Priority.Low);
            OnPlayerDisconnectEvent.Register(HandlePlayerDisconnect, Priority.Low);
        }

        public override void Unload(bool shutdown)
        {
            Command.Unregister(Command.Find("TimeManagement"));
            Server.MainScheduler.Cancel(task);
            OnPlayerConnectEvent.Unregister(HandlePlayerConnect);
            OnPlayerDisconnectEvent.Unregister(HandlePlayerDisconnect);
        }

        private void TMCallback(SchedulerTask task)
        {
            // TODO: CLEAN DATA
            DeleteOldTMData();
            MessageOnlinePlayers();
        }

        private void HandlePlayerConnect(Player p) { LogEvent(p, "+"); }
        private void HandlePlayerDisconnect(Player p, string reason) { LogEvent(p, "-"); }

        // Adds or alters a data entry to our timeManagementTable.txt. Works by appending the login/logout event
        private void LogEvent(Player p, string symbol)
        {
            string pName = p.name;
            string appendToData = String.Format(",{0}{1}", symbol, DateTime.UtcNow.ToString().Replace(' ', '_'));
            if (TMTable.Contains(pName))
            {
                string data = TMTable.FindData(pName);
                data += appendToData;
                TMTable.Update(pName, data);
            }
            else
            {
                string data = appendToData.Remove(0, 1);    // Remove comma if it's the first event
                TMTable.Update(pName, data);
            }
            TMTable.Save();
        }

        // HELPER FUNCTIONS

        private void DeleteOldTMData()
        {
            foreach (string pname in TMTable.AllNames())
            {
                string data = TMTable.FindData(pname);
                List<string> timeStamps = new List<string>(data.Split(','));

                // Remove all empty lines (with no login/logout data)
                if (timeStamps.Count == 0)
                {
                    TMTable.Remove(pname);
                    continue;
                }

                // Be fine with just a single login event
                if (timeStamps.Count == 1)
                {
                    continue;
                }

                // Aside: at this point our dataStamps list should start with a login event and alternate between login and logout ones
                // The data will have at least 2 entries, so no need to worry about removing from an empty list

                string oldestLogout = timeStamps[1].Substring(1, timeStamps[1].Length - 1).Replace('_', ' ');
                TimeSpan sinceOldestLogout = DateTime.UtcNow - DateTime.Parse(oldestLogout);

                // If the first logout is more than 24 hours ago delete it and the first login (and do this iteratively in case someone relogged several times around that time)
                while (sinceOldestLogout.TotalSeconds > DAY_IN_SECONDS && timeStamps.Count >= 2)
                {
                    timeStamps.RemoveAt(0);
                    timeStamps.RemoveAt(0);
                }

                // Remove all empty lines a second time (the above procedure can make an entry empty)
                if (timeStamps.Count == 0)
                {
                    TMTable.Remove(pname);
                    continue;
                }

                data = String.Join(",", timeStamps);
                TMTable.Update(pname, data);
            }
            TMTable.Save();
        }

        private int GetTotalTimeSpent(string pname, string mode)
        {
            int timeSpent = 0;

            string data = TMTable.FindData(pname);
            List<string> timeStamps = new List<string>(data.Split(','));

            if (mode == "current")
            {
                string loginDate = timeStamps[timeStamps.Count - 1];
                if (loginDate[0] == '+')
                {
                    string login = loginDate.Substring(1, loginDate.Length - 1).Replace('_', ' ');
                    TimeSpan sinceLogin = DateTime.UtcNow - DateTime.Parse(login);
                    timeSpent = (int)sinceLogin.TotalSeconds;
                }
            }
            else if (mode == "daily")
            {
                DateTime dayAgo = DateTime.UtcNow.AddSeconds(-DAY_IN_SECONDS);

                string oldestLogin = timeStamps[0].Substring(1, timeStamps[0].Length - 1).Replace('_', ' ');
                TimeSpan sinceOldestLogin = DateTime.UtcNow - DateTime.Parse(oldestLogin);

                // Edge case - oldest login is older than a day. Pretend it's exactly a day old
                if (sinceOldestLogin.TotalSeconds > DAY_IN_SECONDS)
                {
                    timeStamps[0] = '+' + dayAgo.ToString().Replace(' ', '_');
                }

                // Edge case - Last entry is a login event (it should be of course). Pretend there's a logout event at the end that's the current date
                if (timeStamps[timeStamps.Count - 1][0] == '+')
                {
                    string newestLogout = "-" + DateTime.UtcNow.ToString().Replace(' ', '_');
                    timeStamps.Add(newestLogout);
                }

                timeSpent = 0;

                for (int i = 0; i < timeStamps.Count; i += 2)
                {
                    string login = timeStamps[i].Substring(1, timeStamps[i].Length - 1).Replace('_', ' ');
                    string logout = timeStamps[i + 1].Substring(1, timeStamps[i + 1].Length - 1).Replace('_', ' ');
                    DateTime loginDaytime = DateTime.Parse(login);
                    DateTime logoutDaytime = DateTime.Parse(logout);
                    TimeSpan timeOnline = logoutDaytime - loginDaytime;
                    timeSpent += (int)timeOnline.TotalSeconds;
                }
            }
            return timeSpent;
        }

        private List<string> CleanTimeStamps(List<string> stamps, string pName)
        {
            // Get rid of all the useless logout timestamps at the start (although this is really just an extra check, in theory there shouldn't be any)
            while (stamps.Count != 0 && stamps[0][0] == '-')
            {
                stamps.RemoveAt(0);
            }

            // TODO: MAKE THE BELOW A LOOP THAT TRAVERSES THE STRING

            // Update if the first two entries are both logins or logouts (that would be strange indeed)
            if (stamps.Count >= 2 && stamps[0][0] == stamps[1][0])
            {
                stamps.RemoveAt(0);
            }

            return stamps;
        }

        private void MessageOnlinePlayers()
        {
            Player[] players = PlayerInfo.Online.Items;
            foreach (Player p in players)
            {
                string mode = playerTMSettings.FindData(p.name);
                if (mode == null) { continue; }

                int timeSpent = GetTotalTimeSpent(p.name, mode);
                string timeSpentMessage = TMMessages(timeSpent);

                if (timeSpentMessage == "") { return; }

                if (mode == "current")
                {
                    p.Message(TMMessages(timeSpent) + " since you logged in.");
                }
                else if (mode == "daily")
                {
                    p.Message(TMMessages(timeSpent) + " in the last 24 hours.");
                }
            }
        }

        private string TMMessages(int seconds)
        {
            /* Special magic that prevents me having to write "ïf" to determine intervals
             * For instance (600 - HEARTBEAT_TM / 2 <= seconds && seconds < 600 + HEARTBEAT_TM / 2)
             * is the same as magicConstant == 600
            */
            double X = (seconds + HEARTBEAT_TM / 2) / HEARTBEAT_TM;
            int magicConstant = (int)(Math.Floor(X)) * HEARTBEAT_TM;

            switch (magicConstant)
            {
                case 300:
                    return "You have been online for 5 minutes";
                case 600:
                    return "You have been online for 10 minutes";
                case 1200:
                    return "You have been online for 20 minutes";
                case 1800:
                    return "You have been online for 30 minutes";
                case 2700:
                    return "You have been online for 45 minutes";
                case 3600:
                    return "You have been online for an hour";
                case 7200:
                    return "You have been online for two hours";
                case 10800:
                    return "Take good care of yourself! You have been online for three hours";
                case 14400:
                    return "Slow down there buckaroo, you have been online for four hours";
                case 18000:
                    return "Take a break! You have been online for five hours";
                case 21600:
                    return "Your time on this planet is short, so take a break, as you have been online for six hours";
                default:
                    if (magicConstant % HOUR_IN_SECONDS == 0)
                    {
                        string hours = (magicConstant / HOUR_IN_SECONDS).ToString();
                        return String.Format("Go outside! You have been online for {0} hours", hours);
                    }
                    else
                    {
                        return "";
                    }
            }
        }
    }


    public sealed class CmdTimeManagement : Command2
    {
        public override string name { get { return "TimeManagement"; } }
        public override string shortcut { get { return "TM"; } }
        public override string type { get { return CommandTypes.Other; } }

        public override void Use(Player p, string message)
        {
            string[] args = message.SplitSpaces();
            if (args.Length != 1) { Help(p); return; }
            args[0] = args[0].ToLowerInvariant();

            switch (args[0])
            {
                case "none":
                    TimeManagement.playerTMSettings.Remove(p.name);
                    p.Message("TM mode set to none (no notifications)");
                    break;
                case "daily":
                    TimeManagement.playerTMSettings.Update(p.name, "daily");
                    p.Message("TM mode set to daily");
                    break;
                case "current":
                    TimeManagement.playerTMSettings.Update(p.name, "current");
                    p.Message("TM mode set to current");
                    break;
                default:
                    Help(p);
                    break;
            }
        }

        public override void Help(Player p)
        {
            p.Message("%T/TimeManagement [daily/current/none]%H- Toggles time management mode");
            p.Message("Daily mode keeps you up to date on your activity in the last 24 hours.");
            p.Message("Current mode keeps you up to date on your activity since you logged in.");
            p.Message("Type '/TimeManagement none' to reset to none.");
        }
    }
}
