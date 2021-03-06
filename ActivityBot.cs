/*
 * Pings players on Discord if there are more than TRESHOLD_PLAYERS online
 * Special thanks to Panda and Venk for hosting their plugins in public repos for people to analyze
 * We add a file lastActivityPing.txt to keep track of the last time we pinged... Why not a local variable? Because I don't want to spam players each time upon restarting the server,
 * and local variables get reset upon each restart
 * 
 * BEFORE USING THIS PLUGIN:
 * To set up your own Discord bot for your own server see https://github.com/UnknownShadow200/MCGalaxy/wiki/Discord-relay-bot
 *
 * OTHER NOTES:
 * We can't use a "rich embed" (the fancy one with titles and all that) because for some reason
 * Discord won't make mentions actually ping within rich embeds. So we send a simple message
 */

using System;
using System.IO;
using System.Text;
using MCGalaxy.Modules.Relay.Discord;
using MCGalaxy.Tasks;

namespace MCGalaxy
{
    public class ActivityBot : Plugin
    {
        /* READ AND EDIT THESE */
        const string CHANNEL_ID = "";      // FILL THIS IN
        const string ROLE_ID = "";         // FILL THIS IN
        const int HEARTBEAT_TIME = 60;     // Default checks playerbase every 60 seconds (seconds!)
        const int IDLE_TIME = 180;         // Default waiting time before pinging again every 180 minutes (minutes!)
        const int THRESHOLD_PLAYERS = 20;  // Minimum threshold # players to trigger the Discord bot



        /* DON'T TOUCH THE REST UNLESS YOU'RE A DEV */
        DateTime lastPing;                  // Last time we pinged
        private readonly object updateLock = new object();  // File locking for writing to lastActivityPing.txt... Probably overkill
        string saveFilePath = "lastActivityPing.txt";

        SchedulerTask task;

        public override string creator { get { return "Opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.3.4"; } } // Highly recommend 1.9.3 or higher
        public override string name { get { return "ActivityBot"; } }

        public override void Load(bool startup)
        {
            ConditionalCreateFile(saveFilePath);
            task = Server.MainScheduler.QueueRepeat(CheckPlayerbaseAndPing, null, TimeSpan.FromSeconds(HEARTBEAT_TIME));
        }

        public override void Unload(bool shutdown)
        {
            Server.MainScheduler.Cancel(task);
        }

        // The crux of the plugin. Checks if there's enough players and does the pinging
        public void CheckPlayerbaseAndPing(SchedulerTask task)
        {
            lastPing = ReadLastPing(saveFilePath);

            // Skip the rest if too few players or too recent ping
            if (!ShouldBotPing(lastPing)) return;

            DiscordBot discBot = DiscordPlugin.Bot;
            try
            {
                EmbedPing(discBot, CHANNEL_ID);
            }
            catch (Exception e)
            {
                Logger.Log(LogType.Debug, String.Format("Failed to Discord ping the activity bot. ERROR: {0}", e.StackTrace));
            }

            UpdateLastPing(saveFilePath, DateTime.UtcNow);
        }

        // HELPER FUNCTIONS
        // Creates {saveFilePath}.txt if it doesn't exist already and throws in the current time
        private void ConditionalCreateFile(string path)
        {
            if (!File.Exists(path))
            {
                File.Create(path).Close();
                Logger.Log(LogType.SystemActivity, "CREATED NEW: " + path);
                // Why subtract the idle time below? Because if we didn't the plugin wouldn't think to ping for IDLE_TIME minutes when loaded for the first time
                UpdateLastPing(saveFilePath, DateTime.UtcNow.AddMinutes(-IDLE_TIME));
            }
        }

        // Test if bot should ping
        private bool ShouldBotPing(DateTime lastPing)
        {
            // Are enough players online to trigger the bot?
            int players_online = PlayerInfo.Online.Items.Length;
            if (players_online < THRESHOLD_PLAYERS) return false;

            // Has the bot not already been triggered recently?
            TimeSpan idleTime = DateTime.UtcNow - lastPing;
            if (idleTime.TotalMinutes < IDLE_TIME) return false;
            return true;
        }

        // Does the pinging
        public void EmbedPing(DiscordBot disc, string channelID)
        {
            Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds; // Thank you "bizzehdee" on stackexchange
            string msg = String.Format("There are {0} players online! <@&{1}>\n" +
                                        "Sent <t:{2}> local time\n" +
                                        "_ _ _ _ _ _ _ _ _ _", PlayerInfo.Online.Items.Length, ROLE_ID, unixTimestamp);
            ChannelSendMessage basicEmbed = new ChannelSendMessage(channelID, msg);
            disc.Send(basicEmbed);
        }

        // Updates the lastActivityPing.txt file (not proud of that name)
        private void UpdateLastPing(string path, DateTime time)
        {
            lock (updateLock)
            {
                try
                {
                    string[] input = { time.ToString() };   // Bit hacky, but just writes last ping time into the text file
                    File.WriteAllLines(path, input);
                }
                catch (Exception e)
                {
                    Logger.Log(LogType.Debug, String.Format("Error writing to {0}.txt. ERROR: {1}", saveFilePath, e.StackTrace));
                }
            }
        }

        // Reads the last ping from the lastActivityPing.txt file
        private DateTime ReadLastPing(string path)
        {
            string line = "";
            try
            {
                using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
                {
                    line = sr.ReadLine();
                }
            }
            catch (Exception e)
            {
                Logger.Log(LogType.Debug, String.Format("Error reading from {0}.txt. ERROR: {1}", saveFilePath, e.StackTrace));
            }
            return DateTime.Parse(line);
        }
    }
}
