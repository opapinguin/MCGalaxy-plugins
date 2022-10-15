//reference System.dll

using MCGalaxy.Events.LevelEvents;
using MCGalaxy.Maths;
using MCGalaxy.Tasks;
using MCGalaxy.Events.PlayerEvents;
using System;
using System.Collections.Generic;
using System.IO;
using BlockID = System.UInt16;

namespace MCGalaxy
{
    /***********************
    * ANIMATION DATA TYPES *
    ************************/

    /* I opted for cache locality, insofar as we can achieve that in C#
     It's not the most compact way to store these animations but it's fast to read and write and keeps related data packed */


    // Contains all the animations for a map
    public sealed class MapAnimation
    {
        public MapAnimation(bool running, ushort currentTick, ushort numLoops)
        {
            this.bRunning = running; this._currentTick = currentTick; this._numLoops = numLoops; this._blocks = new List<AnimBlock>();
        }
        public bool bRunning;
        public ushort _currentTick;
        public List<AnimBlock> _blocks;
        public ushort _numLoops;
    }

    // An animated block at a specific coordinate. Can contain many animated loops
    public sealed class AnimBlock
    {
        public AnimBlock(ushort x, ushort y, ushort z, BlockID block)
        {
            this._x = x; this._y = y; this._z = z; this._currentBlock = block; this._loopList = new SortedList<ushort, AnimLoop>();
        }
        public ushort _x;
        public ushort _y;
        public ushort _z;
        public BlockID _currentBlock;  // Currently visible block
        public SortedList<ushort, AnimLoop> _loopList; // List sorted by index
    }

    // A single loop
    public sealed class AnimLoop
    {
        public AnimLoop(ushort stride, ushort width, short startTick, ushort endTick, BlockID block)
        {
            this._stride = stride; this._width = width; this._startTick = startTick; this._endTick = endTick; this._block = block;
        }
        public ushort _stride;         // The period of the loop
        public ushort _width;          // The length of time we keep seeing the loop
        public short _startTick;      // Animation offset
        public ushort _endTick;        // Animation end
        public BlockID _block;
    }

    /**********
     * PLUGIN *
     **********/

    public class AnimationsPlugin : Plugin
    {
        const int SAVE_DELAY = 60 * 5;  // We save all animations every 5 minutes

        public override string creator { get { return "Opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.4.2"; } }
        public override string name { get { return "Animations"; } }

        SchedulerTask taskSave;
        public override void Load(bool startup)
        {
            CreateAnimsDir();

            StartAnimations();

            OnLevelLoadedEvent.Register(HandleLevelLoaded, Priority.Normal);
            OnLevelUnloadEvent.Register(HandleLevelUnload, Priority.Normal);
            OnPlayerClickEvent.Register(HandlePlayerClick, Priority.Normal);
            OnJoinedLevelEvent.Register(HandleJoinLevel, Priority.Normal);

            taskSave = Server.MainScheduler.QueueRepeat(SaveAllAnimations, null, TimeSpan.FromSeconds(SAVE_DELAY));

            AnimationHandler.Activate();
            InitializeAnimDict();

            Command.Register(new CmdAnimation());
        }

        public override void Unload(bool shutdown)
        {
            OnLevelLoadedEvent.Unregister(HandleLevelLoaded);
            OnLevelUnloadEvent.Unregister(HandleLevelUnload);
            OnPlayerClickEvent.Unregister(HandlePlayerClick);
            OnJoinedLevelEvent.Unregister(HandleJoinLevel);

            Server.MainScheduler.Cancel(taskSave);
            AnimationHandler.Deactivate();

            Command.Unregister(Command2.Find("Animation"));
        }

        // Creates the animation directory
        private void CreateAnimsDir()
        {
            if (!Directory.Exists("Animations"))
            {
                try
                {
                    Directory.CreateDirectory("Animations");
                }
                catch (Exception e)
                {
                    Logger.Log(LogType.Error, "Failed to create Animations folder");
                    Logger.Log(LogType.Error, e.StackTrace);
                }
            }
        }

        /******************
         * EVENT HANDLERS *
         ******************/

        private void InitializeAnimDict()
        {
            foreach (Level level in LevelInfo.Loaded.Items)
            {
                if (File.Exists(String.Format("Animations/{0}+animation.txt", level.name)))
                {
                    ReadAnimation(level);
                }
            }
        }

        private void HandleLevelLoaded(Level level)
        {
            ReadAnimation(level);
        }

        private void HandleLevelUnload(Level level, ref bool cancel)
        {
            SaveAnimation(level);
            AnimationHandler.RemoveFromActiveLevels(level);
        }

        private void HandlePlayerClick(Player p, MouseButton button, MouseAction action, ushort yaw, ushort pitch, byte entity, ushort x, ushort y, ushort z, TargetBlockFace face)
        {
            AnimationHandler.SendCurrentFrameBlock(p, x, y, z);
        }

        private void HandleJoinLevel(Player p, Level prevLevel, Level level, ref bool announce)
        {
            AnimationHandler.SendCurrentFrame(p, level);
        }

        /******************************
        * FILE AND ANIMATION HANDLING *
        *******************************/

        // Starts all animations for maps that have them
        private void StartAnimations()
        {
            foreach (Level level in LevelInfo.Loaded.Items)
            {
                ReadAnimation(level);
            }
        }

        public static void SaveAllAnimations(SchedulerTask task)
        {
            foreach (Level level in LevelInfo.Loaded.Items)
            {
                SaveAnimation(level);
            }
        }

        // Read the animation file for a level if it exists, then sends it to the level's MapAnimation (creates it if it does not exist)
        public static void ReadAnimation(Level level)
        {
            // File is ordered as <x> <y> <z> <index> <stride> <width> <start> <end> <blockID>
            List<String> animFile;
            try
            {
                if (File.Exists(String.Format("Animations/{0}+animation.txt", level.name)))
                {
                    string[] logFile = File.ReadAllLines(String.Format("Animations/{0}+animation.txt", level.name));
                    animFile = new List<string>(logFile);
                }
                else
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Logger.Log(LogType.Error, String.Format("Could not read Animations/{0}+animation.txt", level.name));
                Logger.Log(LogType.Error, e.StackTrace);
                return;
            }

            // Write into the MapAnimation for the level
            foreach (String line in animFile)
            {
                string[] l = line.Split(' ');
                try
                {
                    AnimationHandler.AddLoop(level, Convert.ToUInt16(l[0]), Convert.ToUInt16(l[1]), Convert.ToUInt16(l[2]), Convert.ToUInt16(l[3]), Convert.ToUInt16(l[4]), Convert.ToUInt16(l[5]), Convert.ToInt16(l[6]), Convert.ToUInt16(l[7]), Convert.ToUInt16(l[8]));
                }
                catch (Exception e)
                {
                    Logger.Log(LogType.Error, String.Format("Could not read line \" {0} \" from Animations/{1}+animation.txt", line, level.name));
                    Logger.Log(LogType.Error, e.StackTrace);
                    return;
                }
            }
        }

        // Write the animation thus far to [level]+animation.txt in ./Animations
        public static void SaveAnimation(Level level)
        {
            if (!AnimationHandler.HasAnims(level))
            {
                ConditionalDeleteAnimationFile(level);
                return;
            }

            MapAnimation mapAnim = AnimationHandler.dictActiveLevels[level.name];

            if (mapAnim._numLoops == 0)
            {
                ConditionalDeleteAnimationFile(level);
                return;
            }

            List<string> lines = new List<string>();
            foreach (AnimBlock animBlock in mapAnim._blocks)
            {
                foreach (var kvp in animBlock._loopList)
                {
                    // File is ordered as <x> <y> <z> <index> <stride> <width> <start> <end> <blockID>
                    lines.Add(String.Format("{0} {1} {2} {3} {4} {5} {6} {7} {8}", animBlock._x, animBlock._y, animBlock._z, kvp.Key, kvp.Value._stride, kvp.Value._width, kvp.Value._startTick, kvp.Value._endTick, kvp.Value._block));
                }
            }

            File.WriteAllLines(String.Format("Animations/{0}+animation.txt", level.name, false), lines.ToArray());     // TODO: Make this async if it turns out slow to write all animations
        }

        // Deletes the animation file [level]+animation.txt in ./Animations if it exists
        public static void ConditionalDeleteAnimationFile(Level level)
        {
            if (AnimationExists(level))
            {
                try
                {
                    File.Delete(String.Format("Animations/{0}+animation.txt", level.name));
                }
                catch (Exception e)
                {
                    Logger.Log(LogType.Error, String.Format("Failed to delete file \"Animations/{0}+animation.txt\"", level.name));
                    Logger.Log(LogType.Error, e.StackTrace);
                }
            }
        }

        // Checks if [level]+animations.txt exists in ./Animations
        public static bool AnimationExists(Level level)
        {
            return File.Exists(String.Format("Animations/{0}+animation.txt", level.name));
        }

    }

    // Handles animation scheduling across maps as well as adding loops to/removing loops from animations in these maps
    // Note that we need to keep maps in this static class, because maps are not passed around by reference but rather many instances
    // Are made for each player, so that using the ExtrasCollection for each map will not be viable
    public static class AnimationHandler
    {
        const ushort TICKS_PER_SECOND = 10;
        static Scheduler instance;
        static readonly object activateLock = new object();
        static readonly object deactivateLock = new object();
        static SchedulerTask task;
        public static Dictionary<string, MapAnimation> dictActiveLevels = new Dictionary<string, MapAnimation>();  // Levels with map animations

        internal static void Activate()
        {
            lock (activateLock)
            {
                if (instance != null) return;

                instance = new Scheduler("AnimationScheduler");
                task = instance.QueueRepeat(AnimationsTick, null, TimeSpan.FromMilliseconds(1000 / TICKS_PER_SECOND));
            }
        }

        internal static void Deactivate()
        {
            lock (deactivateLock)
            {
                if (instance != null)
                {
                    instance.Cancel(task);
                }
            }
            dictActiveLevels.Clear();
        }

        // Keeps track of animations in this level
        internal static bool HasAnims(Level level)
        {
            return dictActiveLevels.ContainsKey(level.name);
        }

        internal static void SetAnims(Level level, MapAnimation mapAnim)
        {
            dictActiveLevels[level.name] = mapAnim;
        }

        // Adds a level to the active levels
        internal static void AddToActiveLevels(Level level, MapAnimation mapAnim)
        {
            if (!HasAnims(level))
            {
                dictActiveLevels.Add(level.name, mapAnim);
            }
        }

        // Removes a level from the active levels
        internal static void RemoveFromActiveLevels(Level level)
        {
            if (HasAnims(level))
            {
                dictActiveLevels.Remove(level.name);
            }
        }

        // Handles animation across all maps
        static void AnimationsTick(SchedulerTask task)
        {
            foreach (var kvp in dictActiveLevels)
            {
                Update(kvp.Key);
            }
        }

        // Sends current frame block identity. Useful when marking or breaking a block, or on level join event
        internal static void SendCurrentFrame(Player p, Level level)
        {
            MapAnimation mapAnimation;
            if (HasAnims(level))
            {
                mapAnimation = dictActiveLevels[level.name];
            } else
            {
                return;
            }

            foreach (AnimBlock aBlock in mapAnimation._blocks)
            {
                BlockID CurrentBlock = GetCurrentBlock(aBlock._loopList, mapAnimation._currentTick);
                if (CurrentBlock != BlockID.MaxValue)
                {
                    p.SendBlockchange(aBlock._x, aBlock._y, aBlock._z, CurrentBlock);
                }
            }
        }

        // Sends current frame block identity for a specific block
        internal static void SendCurrentFrameBlock(Player p, ushort x, ushort y, ushort z)
        {
            MapAnimation mapAnimation;
            if (HasAnims(p.level))
            {
                mapAnimation = dictActiveLevels[p.level.name];
            } else
            {
                return;
            }

            foreach (AnimBlock aBlock in mapAnimation._blocks)
            {
                if (aBlock._x == x && aBlock._y == y && aBlock._z == z)
                {
                    BlockID CurrentBlock = GetCurrentBlock(aBlock._loopList, mapAnimation._currentTick);
                    if (CurrentBlock != BlockID.MaxValue)
                    {
                        p.SendBlockchange(aBlock._x, aBlock._y, aBlock._z, CurrentBlock);
                    }
                    return;
                }
            }
        }

        // Handles animation on a single map
        static void Update(string level)
        {
            MapAnimation mapAnimation = dictActiveLevels[level];
            List<Player> players = LevelInfo.FindExact(level).getPlayers();      // TODO: Could cache this
            if (mapAnimation.bRunning)
            {
                foreach (AnimBlock animBlock in mapAnimation._blocks)
                {
                    BlockID prevBlock = animBlock._currentBlock;    // Previous frame's block
                    BlockID currentBlock = GetCurrentBlock(animBlock._loopList, mapAnimation._currentTick); // Current frame's block

                    if (currentBlock == prevBlock) // Don't have to send block changes if the block doesn't change
                    {
                        continue;
                    }

                    foreach (Player pl in players)
                    {
                        if (pl.Extras.Contains("ShowAnim") && !(bool)pl.Extras["ShowAnim"]) // If the player has reveal animations turned on (animations as red blocks)
                        {
                            continue;
                        }

                        if (currentBlock == System.UInt16.MaxValue)   // Loop is in off state
                        {
                            pl.RevertBlock(animBlock._x, animBlock._y, animBlock._z);
                        }
                        else  // Loop is in on state
                        {
                            pl.SendBlockchange(animBlock._x, animBlock._y, animBlock._z, currentBlock);
                        }
                    }
                    animBlock._currentBlock = currentBlock;
                }

                mapAnimation._currentTick += 1;

                if (mapAnimation._currentTick == ushort.MaxValue)
                {
                    mapAnimation._currentTick = 0;
                }
            }
        }

        // Gets the currently visible block given the tick across an array of loops. Returns 65535 if no block is visible at that frame
        private static BlockID GetCurrentBlock(SortedList<ushort, AnimLoop> loops, ushort tick)
        {
            BlockID loopActiveBlock;
            foreach (var kvp in loops)  // kvp = (index, AnimLoop). Note that this loops from lowest to highest index automatically
            {
                loopActiveBlock = CurrentBlock(kvp.Value, tick);    // The block visible during the current loop
                if (loopActiveBlock != System.UInt16.MaxValue)
                {
                    return loopActiveBlock;
                }
            }

            return System.UInt16.MaxValue;
        }

        // Gets the currently visible block for a single loop. Returns 65535 if the loop is "off" in a frame
        private static BlockID CurrentBlock(AnimLoop loop, ushort tick)
        {
            if (tick < loop._startTick)
            {
                return System.UInt16.MaxValue;
            }

            if (tick > loop._endTick)
            {
                if (((loop._endTick - loop._startTick) % loop._stride) < loop._width)       // If the block is active during the loop  TODO: See if it should say tick +1 or just tick
                {
                    return loop._block;
                }
                return System.UInt16.MaxValue;
            }

            if (((tick - loop._startTick) % loop._stride) < loop._width)       // If the block is active during the loop  TODO: See if it should say tick +1 or just tick
            {
                return loop._block;
            }

            return System.UInt16.MaxValue;
        }

        // Initializes a map animation if it does not already exist. Adds it to the active levels
        private static void ConditionalAddMapAnimation(Level level)
        {
            if (!HasAnims(level))
            {
                MapAnimation mapAnimation = new MapAnimation(true, 0, 0)
                {
                    _blocks = new List<AnimBlock>()
                };

                SetAnims(level, mapAnimation);
            }
        }

        // Remove a map animation if it exists. Removes it from the active levels
        private static void ConditionalRemoveMapAnimation(Level level)
        {
            if (HasAnims(level))
            {
                RemoveFromActiveLevels(level);
            }
        }

        // Places a loop in a level. If "all" is set to true, replaces all loops for a block
        internal static void Place(Level level, ushort x, ushort y, ushort z, ushort idx, ushort stride, ushort width, short startTick, ushort endTick, BlockID block, bool all)
        {
            ConditionalAddMapAnimation(level);

            MapAnimation mapAnimation = AnimationHandler.dictActiveLevels[level.name];

            if (mapAnimation._numLoops == ushort.MaxValue)
            {
                return;
            }

            foreach (AnimBlock animB in mapAnimation._blocks)
            {
                if (animB._x == x && animB._y == y && animB._z == z)
                {
                    AnimLoop loop = new AnimLoop(stride, width, startTick, endTick, block);

                    if (all)
                    {
                        mapAnimation._numLoops -= (ushort)animB._loopList.Count;
                        animB._loopList.Clear();
                    }

                    // If idx is maxvalue, it's a special value indicating we fill in the first available key (e.g. for use in /anim add [stride] [wdidth])
                    if (idx == ushort.MaxValue)
                    {
                        ushort i = 0;

                        while (animB._loopList.Keys.Contains(i))
                        {
                            i += 1;
                        }
                        idx = i;
                    }

                    // Replace loop if exists. Otherwise overwrite it
                    if (animB._loopList.Keys.Contains(idx))
                    {
                        animB._loopList[idx] = loop;
                    } else
                    {
                        animB._loopList.Add(idx, loop);
                        mapAnimation._numLoops += 1;
                    }

                    return;
                }
            }

            // If we're here, it means we're not overwriting a block but creating a new one
            AnimBlock animBlock = new AnimBlock(x, y, z, block);
            animBlock._loopList.Add(1, new AnimLoop(stride, width, startTick, endTick, block));

            mapAnimation._blocks.Add(animBlock);
            mapAnimation._numLoops += 1;
        }

        // Deletes a loop in a level. If all is true, deletes all loops for a block
        internal static void Delete(Level level, ushort x, ushort y, ushort z, ushort idx, bool all)
        {
            if (!AnimationHandler.HasAnims(level)) return;

            MapAnimation mapAnimation = AnimationHandler.dictActiveLevels[level.name];

            // Looks like a massive loop but takes no more than O(number of animation blocks) operations
            foreach (AnimBlock animBlock in mapAnimation._blocks)
            {
                if (!(animBlock._x == x && animBlock._y == y && animBlock._z == z)) { continue; }

                if (all)    // Remove all loops at the location
                {
                    mapAnimation._numLoops -= (ushort)animBlock._loopList.Count;
                    if (mapAnimation._numLoops <= 0)
                    {
                        ConditionalRemoveMapAnimation(level);
                    }

                    mapAnimation._blocks.Remove(animBlock);
                    return;
                }
                else
                {
                    foreach (var kvp in animBlock._loopList)    // Find the loop with the specified index and remove only that
                    {
                        if (kvp.Key == idx)
                        {
                            animBlock._loopList.Remove(kvp.Key);

                            if (animBlock._loopList.Count == 0)
                            {
                                mapAnimation._blocks.Remove(animBlock);
                            }

                            mapAnimation._numLoops -= 1;
                            if (mapAnimation._numLoops <= 0)
                            {
                                ConditionalRemoveMapAnimation(level);
                            }
                            return;
                        }
                    }
                }
            }
        }

        // Adds a loop in a level. Creates animation block for this loop if it does not exist already. If loop index already exists, overwrites it
        internal static void AddLoop(Level level, ushort x, ushort y, ushort z, ushort index, ushort stride, ushort width, short start, ushort end, BlockID block)
        {
            ConditionalAddMapAnimation(level);

            MapAnimation mapAnimation = dictActiveLevels[level.name];

            AnimLoop loop = new AnimLoop(stride, width, start, end, block);

            // Add loop to an existing animation block if it exists
            foreach (AnimBlock aBlock in mapAnimation._blocks)
            {
                if (aBlock._x == x && aBlock._y == y && aBlock._z == z)
                {
                    if (aBlock._loopList.ContainsKey(index))
                    {
                        aBlock._loopList[index] = new AnimLoop(stride, width, start, end, block);
                        return;
                    }
                    else
                    {
                        aBlock._loopList.Add(index, loop);
                        // TODO: Maybe set the animation block's current block here
                        mapAnimation._numLoops += 1;
                        return;
                    }
                }
            }

            // Create the animation block then add the loop
            AnimBlock animBlock = new AnimBlock(x, y, z, block);
            animBlock._loopList.Add(index, loop);
            mapAnimation._blocks.Add(animBlock);

            mapAnimation._numLoops += 1;
        }
    }

    public sealed class CmdAnimation : Command2
    {
        public override string name { get { return "Animation"; } }
        public override string shortcut { get { return "anim"; } }
        public override string type { get { return CommandTypes.Other; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }

        public override void Use(Player p, string message)
        {
            if (!p.level.BuildAccess.CheckAllowed(p))
            {
                p.Message("You do not have permissions to use animations on this level.");
                return;
            }

            string[] args = message.ToLower().SplitSpaces();

            AnimationArgs animArgs = new AnimationArgs();

            /******************
             * COMMAND PARSER *
             ******************/
            switch (args.Length)
            {
                case 1: // "/anim stop", "/anim start", "/anim show", "/anim delete", "/anim save", "/anim restart", "/anim info" (and just "/anim" is also considered length 1)
                    if (args[0] == "stop")          // "/anim stop"
                    {
                        StopAnim(p.level);
                        return;
                    }
                    else if (args[0] == "start")    // "/anim start"
                    {
                        StartAnim(p.level);
                        return;
                    }
                    else if (args[0] == "show")     // "/anim show"
                    {
                        ShowAnim(p.level, ref p);
                        return;
                    }
                    else if (args[0] == "save")     // "/anim save"
                    {
                        AnimationsPlugin.SaveAnimation(p.level);
                        p.Message("Animation saved");
                        return;
                    }
                    else if (args[0] == "delete")   // "anim delete"
                    {
                        p.Message("Mark where you want to delete animations");

                        animArgs._commandCode = (ushort)AnimCommandCode.Delete;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "info")
                    {
                        p.Message("Mark the block you want info of");

                        animArgs._commandCode = (ushort)AnimCommandCode.Info;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "restart")
                    {
                        p.Message("Restarted animation");

                        AnimationHandler.dictActiveLevels[p.level.name]._currentTick = 1;
                    }
                    else if (args[0] == "test")   // TODO: remove this
                    {
                        MapAnimation mapAnim = AnimationHandler.dictActiveLevels[p.level.name];
                        p.Message(mapAnim._numLoops.ToString());
                        p.Message(mapAnim._currentTick.ToString());
                        p.Message(mapAnim.bRunning.ToString());

                        /*
                        foreach (AnimBlock animBlock in mapAnim._blocks)
                        {
                            p.Message("------------");
                            p.Message("Current block: " + animBlock._currentBlock.ToString());
                            foreach (var kvp in animBlock._loopList)
                            {
                                BlockID bl = kvp.Key;
                                AnimLoop l = kvp.Value;
                                p.Message("ID: " + bl.ToString());
                                p.Message("Block: " + l._block.ToString());
                                p.Message("EndTick: "+ l._endTick.ToString());
                                p.Message("StartTick: " + l._startTick.ToString());
                                p.Message("Stride: " + l._stride.ToString());
                                p.Message("Widht: " + l._width.ToString());
                            }
                            p.Message("X: " + animBlock._x.ToString());
                            p.Message("Y: " + animBlock._y.ToString());
                            p.Message("Z: " + animBlock._z.ToString());
                            p.Message("------------");
                            p.SendBlockchange(animBlock._x, animBlock._y, animBlock._z, 1);
                        }
                        */
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 2: // "/anim [stride] [width]", "/anim [delete] [num]", "/anim at [tick]"
                    ushort stride, width, num, tick;
                    if (ushort.TryParse(args[0], out stride) && ushort.TryParse(args[1], out width))    // "/anim [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        } else if (width == 0)
                        {
                            p.Message("Width cannot be 0");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.DtrideWidth;
                        animArgs._width = width;
                        animArgs._endTick = ushort.MaxValue;
                        animArgs._stride = stride;
                        animArgs._startTick = 1;
                        animArgs._idx = 0;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "delete" && ushort.TryParse(args[1], out num))                   // "/anim delete [num]"
                    {
                        p.Message("Mark where you want to delete your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.DeleteNum;
                        animArgs._idx = num;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    } else if (args[0] == "at" && ushort.TryParse(args[1], out tick))        // "/anim at [tick]"
                    {
                        p.Message(String.Format("Set tick to {0}", tick.ToString()));

                        AnimationHandler.dictActiveLevels[p.level.name]._currentTick = tick;
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 3: // "/anim swap [num1] [num2]", "/anim add stride width", "/anim start stride width"
                    ushort idx1, idx2; short start;
                    if (args[0] == "swap" && ushort.TryParse(args[1], out idx1) && ushort.TryParse(args[2], out idx2))   // "/anim swap [num1] [num2]"
                    {
                        p.Message("Mark where you want to perform the swap");

                        animArgs._commandCode = (ushort)AnimCommandCode.SwapNum1Num2;
                        animArgs._idx = idx1;
                        animArgs._idx2 = idx2;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "add" && ushort.TryParse(args[1], out stride) && ushort.TryParse(args[2], out width))  // "/anim add [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.AddStrideWidth;
                        animArgs._stride = stride;
                        animArgs._width = width;
                        animArgs._endTick = ushort.MaxValue;
                        animArgs._startTick = 1;
                        animArgs._idx = ushort.MaxValue;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    } else if (short.TryParse(args[0], out start) && ushort.TryParse(args[1], out stride) && ushort.TryParse(args[2], out width))    // "/anim start stride width"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.StartStrideWidth;
                        animArgs._stride = stride;
                        animArgs._width = width;
                        animArgs._endTick = ushort.MaxValue;
                        animArgs._startTick = start;
                        animArgs._idx = 0;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 4: // "/anim [start] [end] [stride] [width]", "/anim add [num] [stride] [width]"
                    ushort end;
                    if (short.TryParse(args[0], out start) && ushort.TryParse(args[1], out end) && ushort.TryParse(args[2], out stride) && ushort.TryParse(args[3], out width)) // "/anim [start] [end] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        } else if (width == 0)
                        {
                            p.Message("Width cannot be 0");
                            return;
                        } else if (start > end)
                        {
                            p.Message("Start cannot be greater than end!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.StartEndStrideWidth;
                        animArgs._startTick = start;
                        animArgs._endTick = end;
                        animArgs._stride = stride;
                        animArgs._width = width;
                        animArgs._idx = 0;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "add" && ushort.TryParse(args[1], out num) && ushort.TryParse(args[2], out stride) && ushort.TryParse(args[3], out width)) // "/anim add [num] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        } else if (width == 0)
                        {
                            p.Message("Width cannot be 0!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.AddNumStrideWidth;
                        animArgs._stride = stride;
                        animArgs._width = width;
                        animArgs._startTick = 1;
                        animArgs._endTick = ushort.MaxValue;
                        animArgs._idx = num;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 5: // "/anim add [start] [end] [stride] [width]"
                    if (args[0] == "add" && short.TryParse(args[1], out start) && ushort.TryParse(args[2], out end) && ushort.TryParse(args[3], out stride) && ushort.TryParse(args[4], out width)) // "/anim add [start] [end] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        }

                        if (width == 0)
                        {
                            p.Message("Width cannot be 0!");
                            return;
                        }

                        if (start > end)
                        {
                            p.Message("Start cannot be greater than end!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.AddStartEndStrideWidth;
                        animArgs._stride = stride;
                        animArgs._width = width;
                        animArgs._startTick = start;
                        animArgs._endTick = end;
                        animArgs._idx = ushort.MaxValue;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 6: // "/anim [num] [start] [end] [stride] [width]"
                    if (args[0] == "add" && ushort.TryParse(args[1], out num) && short.TryParse(args[2], out start) && ushort.TryParse(args[3], out end) && ushort.TryParse(args[4], out stride) && ushort.TryParse(args[5], out width)) // "/anim add [num] [start] [end] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        } else if (width == 0)
                        {
                            p.Message("Width cannot be 0!");
                            return;
                        }
                        else if (start > end)
                        {
                            p.Message("Start cannot be greater than end!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.AddNumStartEndStrideWidth;
                        animArgs._stride = stride;
                        animArgs._width = width;
                        animArgs._startTick = start;
                        animArgs._endTick = end;
                        animArgs._idx = num;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                default:    //  Number of arguments does not make sense
                    Help(p);
                    break;
            }
            return;
        }

        /*******************
        * HELPER FUNCTIONS *
        ********************/

        // Stores info about which command is being accessed
        public enum AnimCommandCode : ushort
        {
            Delete = 0,
            DtrideWidth = 1,
            DeleteNum = 2,
            SwapNum1Num2 = 3,
            AddStrideWidth = 4,
            StartEndStrideWidth = 5,
            AddNumStrideWidth = 6,
            AddStartEndStrideWidth = 7,
            AddNumStartEndStrideWidth = 8,
            Info = 9,
            StartStrideWidth = 10,
            AddStartStrideWidth = 11
        }

        // Information needed when we mark a block for placing/deleting
        private struct AnimationArgs
        {
            public short _startTick;
            public ushort _endTick;
            public ushort _stride;
            public ushort _width;
            public ushort _idx;
            public ushort _idx2;    // Only reserved for swapping
            public ushort _commandCode; // Command code (see above enum)
        }

        // Once a block is placed this is called. See the definition of AnimationArgs (above) for state members
        private bool PlacedMark(Player p, Vec3S32[] marks, object state, BlockID block)
        {
            ushort x = (ushort)marks[0].X, y = (ushort)marks[0].Y, z = (ushort)marks[0].Z;
            AnimationArgs animArgs = (AnimationArgs)state;

            switch (animArgs._commandCode)
            {
                case (ushort)AnimCommandCode.Delete:
                    DeleteAnimation(p, x, y, z, animArgs._idx, true);
                    break;
                case (ushort)AnimCommandCode.DtrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, true);
                    break;
                case (ushort)AnimCommandCode.DeleteNum:
                    DeleteAnimation(p, x, y, z, animArgs._idx, false);
                    break;
                case (ushort)AnimCommandCode.SwapNum1Num2:
                    SwapAnimation(p, x, y, z, animArgs);
                    break;
                case (ushort)AnimCommandCode.AddStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, false);
                    break;
                case (ushort)AnimCommandCode.StartEndStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, true);
                    break;
                case (ushort)AnimCommandCode.AddNumStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, false);
                    break;
                case (ushort)AnimCommandCode.AddStartEndStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, false);
                    break;
                case (ushort)AnimCommandCode.AddNumStartEndStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, false);
                    break;
                case (ushort)AnimCommandCode.Info:
                    InfoAnim(p, x, y, z);
                    break;
                case (ushort)AnimCommandCode.StartStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, block, true);
                    break;
                default:
                    break;
            }
            return true;
        }

        // Deletes an animation block at (x, y, z). If all is true, then we delete all animations at that position. Else delete that of a specific loop within an animation block with index idx
        private void DeleteAnimation(Player p, ushort x, ushort y, ushort z, ushort idx, bool all)
        {
            AnimationHandler.Delete(p.level, x, y, z, idx, all);
        }

        // Places an animation block at (x, y, z)
        private void PlaceAnimation(Player p, ushort x, ushort y, ushort z, AnimationArgs animArgs, BlockID block, bool all)
        {
            AnimationHandler.Place(p.level, x, y, z, animArgs._idx, animArgs._stride, animArgs._width, animArgs._startTick, animArgs._endTick, block, all);
            p.Message(String.Format("Placed animation block at ({0}, {1}, {2})", x, y, z));
        }

        // Swaps two loops inside an index. IF a loop doesn't exist, it just changes the key in a straightforward fashion
        private void SwapAnimation(Player p, ushort x, ushort y, ushort z, AnimationArgs animArgs)
        {
            ushort idx1 = animArgs._idx;
            ushort idx2 = animArgs._idx2;

            if (!AnimationHandler.HasAnims(p.level)) return;

            if (idx1 == idx2) return;

            MapAnimation mapAnimation = AnimationHandler.dictActiveLevels[p.level.name];

            foreach (AnimBlock animBlock in mapAnimation._blocks)
            {
                if (animBlock._x == x && animBlock._y == y && animBlock._z == z)
                {
                    if (!animBlock._loopList.ContainsKey(idx1) && !animBlock._loopList.ContainsKey(idx2))
                    {
                        return;
                    }
                    else if (!animBlock._loopList.ContainsKey(idx1))    // loopList[idx2] does exist
                    {
                        animBlock._loopList[idx1] = animBlock._loopList[idx2];
                        animBlock._loopList.Remove(idx2);
                    }
                    else if (!animBlock._loopList.ContainsKey(idx2))  // loopList[idx1] does exist
                    {
                        animBlock._loopList[idx2] = animBlock._loopList[idx1];
                        animBlock._loopList.Remove(idx1);
                    }
                    else
                    {
                        AnimLoop loop1 = animBlock._loopList[idx1];
                        AnimLoop tmp = new AnimLoop(loop1._stride, loop1._width, loop1._startTick, loop1._endTick, loop1._block);
                        animBlock._loopList[idx1] = animBlock._loopList[idx2];
                        animBlock._loopList[idx2] = tmp;
                    }
                }
            }
        }

        /*********************************
         * STOP, START, SHOW, SAVE, INFO *
         *********************************/

        void StopAnim(Level level)
        {
            if (AnimationHandler.HasAnims(level))
            {
                AnimationHandler.dictActiveLevels[level.name].bRunning = false;
            }
            return;
        }

        void StartAnim(Level level)
        {
            if (AnimationHandler.HasAnims(level))
            {
                AnimationHandler.dictActiveLevels[level.name].bRunning = true;
            }
            return;
        }

        void ShowAnim(Level level, ref Player p)
        {
            if (!p.Extras.Contains("ShowAnim"))
            {
                p.Extras["ShowAnim"] = true;
            }

            if (AnimationHandler.HasAnims(level))
            {
                MapAnimation mapAnimation = AnimationHandler.dictActiveLevels[level.name];
                if ((bool)p.Extras["ShowAnim"])
                {
                    foreach (AnimBlock animBlock in mapAnimation._blocks)
                    {
                        p.SendBlockchange(animBlock._x, animBlock._y, animBlock._z, Block.Red);
                    }
                    p.Extras["ShowAnim"] = false;
                }
                else
                {
                    p.Extras["ShowAnim"] = true;
                }
            }
            return;
        }

        // Sends the information about a block to the player
        void InfoAnim(Player p, ushort x, ushort y, ushort z)
        {
            Level level = p.level;
            if (AnimationHandler.HasAnims(level))
            {
                MapAnimation mapAnimation = AnimationHandler.dictActiveLevels[level.name];

                AnimBlock selection = new AnimBlock(x, y, z, Block.Air)
                {
                    _loopList = null
                };

                foreach (AnimBlock animBlock in mapAnimation._blocks)
                {
                    if (animBlock._x == x && animBlock._y == y && animBlock._z == z)
                    {
                        selection = animBlock;
                        break;
                    }
                }

                if (selection._loopList == null)
                {
                    p.Message("No information to show.");
                    return;
                }

                // If you made it this far the animation block was found and has something to show
                p.Message("<index> : <stride> <width> <start> <end> <blockID>");
                AnimLoop currentLoop;
                foreach (var kvp in selection._loopList)
                {
                    currentLoop = kvp.Value;
                    p.Message(String.Format("{0} : <{1}> <{2}> <{3}> <{4}> <{5}>", kvp.Key, currentLoop._stride, currentLoop._width, currentLoop._startTick, currentLoop._endTick, currentLoop._block));
                }
            }
            return;
        }

        /***************
        * HELP METHODS *
        ****************/

        public override void Help(Player p)
        {
            Help(p, "");
        }

        public override void Help(Player p, string msg)
        {
            switch (msg)
            {
                case "3":
                    p.Message(@"/animation add [start] [end] [stride] [width]");
                    p.Message(@"Adds an animation that begins on the start tick and ends on the end tick without overwriting.");
                    p.Message(@"/animation add [num] [start] [end] [stride] [width]");
                    p.Message(@"As above but inserted into a specific index (can overwrite at this index)");
                    p.Message(@"/animation add [stride] [width]");
                    p.Message(@"Adds an animation that begins immediately and never ends without overwriting.");
                    p.Message(@"/animation add [num] [stride] [width]");
                    p.Message(@"As above but inserted into a specific index (can overwrite at this index)");
                    p.Message(@"/animation delete [num]");
                    p.Message(@"Deletes a specific animation in a block without deleting all animations in a block.");
                    p.Message(@"/animation swap [num1] [num2]");
                    p.Message(@"Swaps the animation indices. Lower-numbered animations are rendered in front of higher-numbered animations.");
                    break;
                case "2":
                    p.Message(@"/animation stop");
                    p.Message(@"Stops the animation in its current state");
                    p.Message(@"/animation start");
                    p.Message(@"Starts the animation in its current state");
                    p.Message(@"/animation show");
                    p.Message(@"Shows all animated blocks in red");
                    p.Message(@"/animation delete");
                    p.Message(@"Delete an animated block");
                    p.Message(@"/animation info");
                    p.Message(@"Returns all info about the animation block");
                    p.Message(@"/animation save");
                    p.Message(@"Saves the animations on the map");
                    p.Message(@"/animation restart");
                    p.Message(@"Restarts the animation");
                    p.Message(@"/animation at [tick]");
                    p.Message(@"Plays animation at the given tick");
                    p.Message(@"For advanced multi-layered animations, type /help animation 3");
                    break;
                case "0":
                    p.Message(@"Animations let us create blocks that periodically toggle on and off");
                    p.Message(@"When a map is loaded it begins a timer that starts at 1 and ticks forward every 10th of a second");
                    p.Message(@"The animation command uses...");
                    p.Message(@"(1) [start] to indicate which tick to start on");
                    p.Message(@"(2) [end] to indicate which to freeze the animation on");
                    p.Message(@"(3) [stride] to indicate the period of the animation");
                    p.Message(@"(4) [width] to indicate the length of time the block will be visible");
                    p.Message(@"Animations are such loops that are put into animation blocks");
                    p.Message(@"Animation blocks can contain several loops at once. By default we overwrite all loops");
                    p.Message(@"For an animation block with several loops, we render ones with higher indices in front of ones with lower indices");
                    p.Message(@"When these loops are in their ""off"" state, they render the normal block behind them");
                    p.Message(@"The ""add"" flag gives us more control over how to manipulate loops in the same animation block");
                    break;
                default:
                    p.Message(@"For a complete explanation use /help animation 0");
                    p.Message(@"/animation [start] [end] [stride] [width]");
                    p.Message(@"Adds an animation that begins on the start tick and ends on the end tick.");
                    p.Message(@"/animation [start] [stride] [width]");
                    p.Message(@"Adds an animation that begins on the start tick");
                    p.Message(@"/animation [stride] [width]");
                    p.Message(@"Adds an animation that begins immediately and never ends.");
                    p.Message(@"NOTE: a tick is 1/10th of a second)");
                    p.Message(@"For additional information, type /help animation 2");
                    break;
            }
        }
    }
}
