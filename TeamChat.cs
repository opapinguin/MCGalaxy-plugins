using MCGalaxy.Games;
using MCGalaxy;
using MCGalaxy.Events.PlayerEvents;

namespace Core
{
    public sealed class TeamChat : Plugin
    {
        public override string creator { get { return "opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.1.2"; } }   // Completely arbitrary version number. Probably works for most.
        public override string name { get { return "TeamChat"; } }

        public override void Load(bool startup)
        {
            Command.Register(new CmdTeamChat());
            OnPlayerChatEvent.Register(HandleTeamChat, Priority.High);
        }

        public override void Unload(bool shutdown)
        {
            Command.Unregister(Command.Find("TeamChat"));
            OnPlayerChatEvent.Unregister(HandleTeamChat);
        }

        void HandleTeamChat(Player p, string message)
        {
            if (p.Extras.GetBoolean("tc", false))
            {
                Team team = p.Game.Team;
                team.Message(p, message);
                p.cancelchat = true;
            }
        }
    }

    public sealed class CmdTeamChat : Command2
    {
        public override string name { get { return "TeamChat"; } }
        public override bool SuperUseable { get { return false; } }
        public override string shortcut { get { return "tc"; } }
        public override string type { get { return CommandTypes.Games; } }
        public override void Use(Player p, string message)
        {
            Team team = p.Game.Team;
            if (team == null)
            {
                p.Message("You need to be in a team first.");
                return;
            }

            if (p.Extras.GetBoolean("tc", false))
            {
                p.Extras["tc"] = false;
                p.Message("Team chat is off");
            }
            else
            {
                p.Extras["tc"] = true;
                p.Message("Team chat is on");
            }
        }
        public override void Help(Player p)
        {
            p.Message("&T/TeamChat &H- Toggles team chat.");
        }
    }
}