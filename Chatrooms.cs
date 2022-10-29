//reference System.Core.dll

/*
 * Chatroom plugin - creates chatrooms, light-weight teams but temporary
 * Chatrooms die when they are out of use, specifically when all players are offline
 * But before it happens offline players are cached in case they return
 */

using System;
using System.Collections.Generic;
using System.Linq;
using MCGalaxy.Events.PlayerEvents;

namespace MCGalaxy
{
    /***********************
     * CHATROOM DATA TYPES *
     ***********************/

    sealed class Chatroom
    {
        public Player _owner;
        public Dictionary<string, Player> _members;   // Need to use a dictionary to do a comparison
        public Dictionary<string, Player> _membersOffline;    // Ditto
        public string _name;
        public string _code;
        private Random random = new Random();

        public Chatroom(Player p, string name)
        {
            _members = new Dictionary<string, Player>();
            _membersOffline = new Dictionary<string, Player>();
            _owner = p;
            _name = name;
            _code = GenerateCode();
        }

        private string GenerateCode()
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var stringChars = new char[5];

            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            return new String(stringChars);
        }

        // Adds a player to the chatroom--also replaces owners if owner is true
        public void AddPlayer(Player p, bool owner)
        {
            if (owner)
            {
                _members.Add(_owner.name, _owner);
                _owner = p;
            } else
            {
                _members.Add(p.name, p);
            }
        }

        // Removes a player from the chatroom
        public void RemovePlayer(Player p)
        {
            if (!_members.ContainsKey(p.name))
            {
                return;
            }

            _members.Remove(p.name);
        }

        // Sends a message to all members of the chatroom
        public void Message(Player p, string message)
        {
            message = "&9- to room - " + p.ColoredName + ": &f" + message;
            foreach (Player pl in _members.Values)
            {
                pl.Message(message);
            }

            _owner.Message(message);
        }

        public void Announce(string message)
        {
            foreach (Player pl in _members.Values)
            {
                pl.Message(message);
            }

            _owner.Message(message);
        }
    }

    /********************
     * CHATROOM HANDLER *
     ********************/

    // Handles loading and unloading all chatrooms
    public static class ChatroomHandler
    {
        private static readonly Dictionary<string, Chatroom> dictChatrooms = new Dictionary<string, Chatroom>();  // Levels with map animations

        // Creates a new chatroom if the chatroom did not already exist. If it does exist, do nothing
        internal static void Create(Player p, String name)
        {
            if (Contains(name))
            {
                return;
            }
            Chatroom chatroom = new Chatroom(p, name);
            dictChatrooms[name] = chatroom;
        }

        // If it exists, deletes a chatroom with the given name
        internal static void Delete(String name)
        {
            if (!Contains(name))
            {
                return;
            }
            dictChatrooms.Remove(name);
        }
        
        // If it exists, deletes a chatroom with a player in it
        internal static void Delete(Player p)
        {
            if (!Contains(p))
            {
                return;
            }

            Chatroom chatroom = Get(p);

            dictChatrooms.Remove(chatroom._name);
        }

        // Gets the chatroom with the player in it. If inexistent, returns null
        internal static Chatroom Get(Player p)
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._members.Keys.Contains(p.name) || cr._owner.name == p.name)
                {
                    return cr;
                }
            }
            return null;
        }

        // Gets the chatroom with the given name. If inexistent, returns null
        internal static Chatroom Get(String name)
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._name == name)
                {
                    return cr;
                }
            }

            return null;
        }

        // Gets the chatroom with the player in the offline list. If inexistent, returns null
        internal static Chatroom GetOffline(Player p)
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._membersOffline.Keys.Contains(p.name))
                {
                    return cr;
                }
            }
            return null;
        }

        // Returns true if and only if there is a chatroom with the given player in it
        internal static bool Contains(Player p)
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._members.Keys.Contains(p.name) || cr._owner.name == p.name)
                {
                    return true;
                }
            }
            return false;
        }

        // Returns true if and only if there is a chatroom with the given player in the offline list
        internal static bool ContainsOffline(Player p)
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._membersOffline.Keys.Contains(p.name))
                {
                    return true;
                }
            }
            return false;
        }

        // Returns true if and only if there is a chatroom with the given name
        internal static bool Contains(String name)
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._name == name)
                {
                    return true;
                }
            }
            return false;
        }

        // Removes chatrooms that have no online players in them
        internal static void RemoveDeadChatrooms()
        {
            foreach (Chatroom cr in dictChatrooms.Values)
            {
                if (cr._members.Count == 0 && !PlayerInfo.Online.Items.Contains(cr._owner))
                {
                    dictChatrooms.Remove(cr._name);
                }
            }
        }
    }

    /***************
     * MAIN PLUGIN *
     ***************/
    public class Chatrooms : Plugin
    {

        public override string creator { get { return "Opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.4.0"; } }
        public override string name { get { return "Chatrooms"; } }

        public override void Load(bool startup)
        {
            Command.Register(new CmdChatroom());
            Command.Register(new CmdChatroomChat());

            OnPlayerChatEvent.Register(HandleChatroomChat, Priority.High);
            OnPlayerConnectEvent.Register(HandleChatroomConnect, Priority.High);
            OnPlayerDisconnectEvent.Register(HandleChatroomDisconnect, Priority.High);
        }

        public override void Unload(bool shutdown)
        {
            Command.Unregister(Command.Find("Chatroom"));
            Command.Unregister(Command.Find("ChatroomChat"));

            OnPlayerChatEvent.Unregister(HandleChatroomChat);
            OnPlayerConnectEvent.Unregister(HandleChatroomConnect);
            OnPlayerDisconnectEvent.Unregister(HandleChatroomDisconnect);
        }

        /******************
         * EVENT HANDLERS *
         ******************/

        // Sends offline listed players to the online list within chatrooms
        private void HandleChatroomConnect(Player p)
        {
            if (ChatroomHandler.ContainsOffline(p))
            {
                if (ChatroomHandler.GetOffline(p)._owner.name != p.name)
                {
                    ChatroomHandler.GetOffline(p)._members.Add(p.name, p);
                }
                ChatroomHandler.GetOffline(p)._membersOffline.Remove(p.name);
            }
        }

        // Sends online listed players to the offline list within chatrooms
        private void HandleChatroomDisconnect(Player p, string reason)
        {
            if (ChatroomHandler.Contains(p))
            {
                ChatroomHandler.Get(p).RemovePlayer(p);
                ChatroomHandler.Get(p)._membersOffline.Add(p.name, p);

                ChatroomHandler.RemoveDeadChatrooms();
            }
        }

        // Handles chatting with chatroom chat on
        private void HandleChatroomChat(Player p, string message)
        {
            if (p.Extras.GetBoolean("crc", false))
            {
                ChatroomHandler.Get(p).Message(p, message);
                p.cancelchat = true;
            }
        }

        public override void Help(Player p)
        {
            base.Help(p);
        }
    }

    /********************
     * CHATROOM COMMAND *
     ********************/
    public sealed class CmdChatroom : Command2
    {
        public override string name { get { return "Chatroom"; } }
        public override string shortcut { get { return "cr"; } }
        public override string type { get { return CommandTypes.Chat; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }

        public override void Use(Player p, string message)
        {
            /******************
             * COMMAND PARSER *
             ******************/

            string[] args = message.SplitSpaces();

            switch(args.Length)
            {
                // "/cr delete", "/cr code", "/cr info" and just "/cr"
                case 1:
                    if (args[0].ToLower() == "delete")                                // "/cr delete"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        Chatroom chatroom = ChatroomHandler.Get(p);
                        if (!(chatroom._owner.name == p.name))
                        {
                            p.Message("Cannot delete this chatroom as you are not the owner");
                            return;
                        }

                        ChatroomHandler.Delete(chatroom._name);
                        chatroom.Announce(String.Format("Deleted chatroom {0}", chatroom._name));
                        return;
                    } else if (args[0].ToLower() == "code")                           // "/cr code"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        Chatroom chatroom = ChatroomHandler.Get(p);

                        p.Message(String.Format("Your chatroom's code is {0}", chatroom._code));
                        return;
                    } else if (args[0].ToLower() == "leave") {                        // "/cr leave"
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        if (ChatroomHandler.Get(p)._owner.name == p.name)
                        {
                            p.Message("Cannot leave this chatroom as you are its owner");
                            return;
                        }

                        Chatroom chatroom = ChatroomHandler.Get(p);

                        chatroom.RemovePlayer(p);
                        p.Message(String.Format("Successfully left chatroom {0}", chatroom._name));
                        return;
                    } else if (args[0].ToLower() == "info")                           // "/cr info"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        Chatroom chatroom = ChatroomHandler.Get(p);

                        string members = "";
                        foreach (Player member in chatroom._members.Values)
                        {
                            members = members + member.ColoredName + "&f, ";
                        }
                        foreach (Player member in chatroom._membersOffline.Values)
                        {
                            members = members + member.ColoredName + "&f, ";
                        }

                        // Remove trailing ", "
                        if (members.Length > 2)
                        {
                            members = members.Substring(members.Length - 2);
                        }

                        p.Message("Info for " + chatroom._name + "&f:");
                        p.Message("Owner: " + chatroom._owner.ColoredName);
                        p.Message("Members: " + members);

                        return;
                    } else
                    {
                        Help(p);
                        return;
                    }
                // "/cr code [code]", "/cr name [new name]", "/cr invite [player]", "/cr owner [new owner]", "/cr info [name]" and "/cr create [name]"
                case 2:
                    if (args[0].ToLower() == "code")        // "/cr code [code]"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        ChatroomHandler.Get(p)._code = args[1];
                        ChatroomHandler.Get(p).Announce(String.Format("Chatroom {0}&f's secret code was changed to {1}", ChatroomHandler.Get(p)._name, ChatroomHandler.Get(p)._code));
                        return;

                    } else if (args[0].ToLower() == "create")                                     // "/cr create [name]"
                    {
                        if (ChatroomHandler.Contains(p))
                        {
                            p.Message("You are already in a chatroom!");
                            return;
                        }

                        if (ChatroomHandler.Contains(args[1]))
                        {
                            p.Message(String.Format("A chatroom with the name {0}&f already exists", args[1]));
                            return;
                        }

                        ChatroomHandler.Create(p, args[1]);

                        p.Message(String.Format("Chatroom {0}&f was created with secret code {1}", args[1], ChatroomHandler.Get(p)._code));
                        return;
                    } else if (args[0].ToLower() == "info")                                                                // "/cr info [name]"
                    {
                        if (!ChatroomHandler.Contains(args[1]))
                        {
                            p.Message("No chatroom with the given name");
                            return;
                        }

                        Chatroom chatroom = ChatroomHandler.Get(args[1]);

                        string members = "";
                        foreach (Player member in chatroom._members.Values)
                        {
                            members = members + member.ColoredName + "&f, ";
                        }
                        foreach (Player member in chatroom._membersOffline.Values)
                        {
                            members = members + member.ColoredName + "&f, ";
                        }

                        // Remove trailing ", "
                        if (members.Length > 2)
                        {
                            members = members.Substring(members.Length - 2);
                        }

                        p.Message("Info for " + chatroom._name + "&f:");
                        p.Message("Owner: " + chatroom._owner.ColoredName);
                        p.Message("Members: " + members);

                        return;
                    }
                    
                    else if (args[0].ToLower() == "name")                // "/cr name [new name]"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        if (ChatroomHandler.Get(p)._owner.name != p.name)
                        {
                            p.Message("Only owners can change the name of a chatroom");
                            return;
                        }

                        if (ChatroomHandler.Contains(args[1]))
                        {
                            p.Message(String.Format("A chatroom with the name {0}&f already exists", args[1]));
                            return;
                        }

                        ChatroomHandler.Get(p)._name = args[1];
                        ChatroomHandler.Get(p).Announce(String.Format("Chatroom name changed to {0}", args[1]));

                        return;
                    } else if (args[0].ToLower() == "invite")         // "/cr invite [player]"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        if (args[1] == p.name)
                        {
                            p.Message("Why are you inviting yourself, you dweeb?");
                            return;
                        }

                        Player receiver = PlayerInfo.FindExact(args[1]);
                        Chatroom chatroom = ChatroomHandler.Get(p);

                        if (receiver == null || !PlayerInfo.Online.Items.Contains(receiver))
                        {
                            p.Message("This player is not online");
                            return;
                        }

                        p.Message(String.Format("Invited {0}&f to chatroom {1}", receiver.ColoredName, chatroom._name));
                        receiver.Message(String.Format("{0}&f invites you to chatroom {1}", p.ColoredName, chatroom._name));
                        receiver.Message(String.Format("Use /chatroom join {0}&f {1} to join", chatroom._name, chatroom._code));
                        return;
                    } else if (args[0].ToLower() == "owner")                    // "/cr owner [new owner]"
                    {
                        if (!ChatroomHandler.Contains(p))
                        {
                            p.Message("You are not in any chatroom");
                            return;
                        }

                        if (ChatroomHandler.Get(p)._owner.name != p.name)
                        {
                            p.Message("You are not the chatroom owner!");
                            return;
                        }

                        if (args[1] == p.name)
                        {
                            p.Message("You are already owner, you dimwit!");
                            return;
                        }

                        if (!ChatroomHandler.Get(p)._members.ContainsKey(args[1]))
                        {
                            p.Message(String.Format("There are no online members with the name {0}", args[1]));
                            return;
                        }

                        Player newOwner = ChatroomHandler.Get(p)._members[args[1]];

                        ChatroomHandler.Get(p)._members.Remove(args[1]);
                        ChatroomHandler.Get(p)._members.Add(p.name, p);
                        ChatroomHandler.Get(p)._owner = newOwner;

                        ChatroomHandler.Get(p).Announce(String.Format("Ownership has been transferred to {0}", newOwner.ColoredName));
                        return;
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                // "/cr join [name] [code]"
                case 3:
                    if (args[0].ToLower() == "join")                                  // "/cr join [name] [code]"
                    {
                        if (ChatroomHandler.Contains(p))
                        {
                            p.Message("You are already in a chatroom!");
                            return;
                        }

                        if (!ChatroomHandler.Contains(args[1]))
                        {
                            p.Message(String.Format("There is no chatroom with the name {0}", args[1]));
                            return;
                        }

                        if (ChatroomHandler.Get(args[1])._code != args[2])
                        {
                            p.Message("Wrong join code!");
                            return;
                        }

                        ChatroomHandler.Get(args[1]).AddPlayer(p, false);
                        return;
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                default:
                    Help(p);
                    return;
            }
        }
        public override void Help(Player p)
        {
            Help(p, "");
        }

        public override void Help(Player p, string message)
        {
            switch(message)
            {
                case "2":
                    p.Message(@"%T/Chatroom owner [new owner]");
                    p.Message(@"%ETransfers ownership of your chatroom");
                    p.Message(@"%T/Chatroom name [new name]");
                    p.Message(@"%EChange the name of your chatroom");
                    p.Message(@"%T/Chatroom code [code]");
                    p.Message(@"%ESets a new code for the chatroom");
                    p.Message(@"%T/Chatroom code");
                    p.Message(@"%EGet the current code");
                    p.Message(@"%T/Chatroom invite [player]");
                    p.Message(@"%EInvites a player to your team");
                    break;
                default:
                    p.Message(@"%T/Chatroom create [name]");
                    p.Message(@"%ECreates a chatroom with the given name");
                    p.Message(@"%T/Chatroom delete");
                    p.Message(@"%EDeletes your chatroom");
                    p.Message(@"%T/Chatroom leave");
                    p.Message(@"%ELeaves your chatroom");
                    p.Message(@"%T/Chatroom join [name] [code]");
                    p.Message(@"%EJoin a chatroom");
                    p.Message(@"%T/Chatroom info");
                    p.Message(@"%ESee chatroom information");
                    p.Message(@"%EFor administrative commands, see /help chatroom 2");
                    p.Message(@"%ESee also /help crc");
                    break;
            }
        }
    }

    /*************************
     * CHATROOM CHAT COMMAND *
     *************************/
    public sealed class CmdChatroomChat : Command2
    {
        public override string name { get { return "ChatroomChat"; } }
        public override string shortcut { get { return "crc"; } }
        public override string type { get { return CommandTypes.Chat; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }

        public override void Use(Player p, string message)
        {
            if (!ChatroomHandler.Contains(p))
            {
                p.Message("You are not currently in any chatroom");
                return;
            }

            // If the message is empty simply toggle chatroom chat
            if (message == "")
            {
                if (p.Extras.GetBoolean("crc", false))
                {
                    p.Extras["crc"] = false;
                    p.Message("Chatroom chat is off");
                }
                else
                {
                    p.Extras["crc"] = true;
                    p.Message("Chatroom chat is on");
                }
                return;
            }

            // Else send a message to the chatroom
            ChatroomHandler.Get(p).Message(p, message);
        }

        public override void Help(Player p)
        {
            p.Message(@"%T/ChatroomChat [message]");
            p.Message(@"%ESends a message to your chatroom");
            p.Message(@"%T/ChatroomChat");
            p.Message(@"%EToggles chatroom chat");
        }
    }
}
