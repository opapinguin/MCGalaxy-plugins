using MCGalaxy.Events.LevelEvents;
using MCGalaxy.Maths;
using MCGalaxy.Tasks;
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
     It's not the most compact way to store these animations but it's easy to read and write and keeps related data packed */


    // Contains all the animations for a map
    struct MapAnimation
    {
        public bool bRunning;
        public int _currentTick;
        public List<AnimBlock> _blocks;
        public ushort numAnimations;
    }

    // An animated block at a specific coordinate. Can contain many animated loops
    struct AnimBlock
    {
        public ushort _x;
        public ushort _y;
        public ushort _z;
        public BlockID _currentBlock;  // Currently visible block
        public SortedList<ushort, AnimLoop> _loopList; // List sorted by index
        public void setCurrentBlock(BlockID block)
        {
            _currentBlock = block;
        }
    }

    // A single loop
    struct AnimLoop
    {
        public int _stride;         // The period of the loop
        public int _width;          // The length of time we keep seeing the loop
        public int _startTick;      // Animation offset
        public int _endTick;        // Animation end
        public BlockID _block;
    }


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
            
            OnLevelLoadedEvent.Register(HandleLevelLoaded, Priority.Normal);
            OnLevelUnloadEvent.Register(HandleLevelUnload, Priority.Normal);

            taskSave = Server.MainScheduler.QueueRepeat(WriteAllAnimations, null, TimeSpan.FromSeconds(SAVE_DELAY));

            AnimationScheduler.Activate();
            AnimationScheduler.UpdateActiveLevels();

            Command.Register(new CmdAnimation());
        }
        public override void Unload(bool shutdown)
        {
            OnLevelLoadedEvent.Unregister(HandleLevelLoaded);
            OnLevelUnloadEvent.Unregister(HandleLevelUnload);

            Server.MainScheduler.Cancel(taskSave);

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
                catch
                {
                    Logger.Log(LogType.Error, "Failed to create Animations folder");
                }
            }
        }

        /******************
         * EVENT HANDLERS *
         ******************/

        private void HandleLevelLoaded(Level level)
        {
            ReadAnimation(level);
            AnimationScheduler.UpdateActiveLevels();
        }

        private void HandleLevelUnload(Level level, ref bool cancel)
        {
            WriteAnimation(level);
            AnimationScheduler.UpdateActiveLevels();
        }

        /*****************
        * FILE HANDLING *
        *****************/

        public static void WriteAllAnimations(SchedulerTask task)
        {
            foreach (Level level in LevelInfo.Loaded.Items)
            {
                WriteAnimation(level);
            }
        }

        public static void ReadAnimation(Level level)
        {
            MapAnimation mapAnim;

            mapAnim._currentTick = 0;
            mapAnim.loops = new List<Loop>();
            mapAnim.bRunning = true;

            // TODO: craete extras animation if it does not exist. Then load the map animation into it (Don't have to create actually)

            return;
        }

        // Write the animation thus far to [level]+animation.txt in ./Animations
        public static void WriteAnimation(Level level)
        {
            if (!level.Extras.Contains("MapAnimation")) return;

            MapAnimation mapAnim = (MapAnimation)level.Extras["MapAnimation"];

            ConditionalCreateAnimation(level);

            // TODO: Write the animation to the corresponding file
            return;
        }

        // Creates the animation file [level]+animation.txt in ./Animations if it does not exist
        public static void ConditionalCreateAnimation(Level level)
        {
            if (!AnimationExists(level))
            {
                File.Create(String.Format("Animations/{0}+animation.txt", level.name));
            }
        }

        // Checks if [level]+animations.txt exists in ./Animations
        public static bool AnimationExists(Level level)
        {
            return File.Exists(String.Format("Animations/{0}+animation.txt", level.name));
        }

    }

    public static class AnimationScheduler
    {
        const ushort TICKS_PER_SECOND = 10;
        static Scheduler instance;
        static readonly object activateLock = new object();
        static List<Level> activeLevels;

        public static void Activate()
        {
            lock (activateLock)
            {
                if (instance != null) return;

                instance = new Scheduler("AnimationScheduler");
                instance.QueueRepeat(AnimationsTick, null, TimeSpan.FromMilliseconds(1000 / TICKS_PER_SECOND));
            }
        }

        // Updates which levels contain animations and which do not
        // It's much more efficient to keep track of levels with animations when needed rather than everytime we call AnimationsTick
        public static void UpdateActiveLevels()
        {
            activeLevels.Clear();
            foreach (Level level in LevelInfo.Loaded.Items)
            {
                if (level.Extras.Contains("MapAnimation"))
                {
                    activeLevels.Add(level);
                }
            }
        }

        // Handles animation across all maps
        static void AnimationsTick(SchedulerTask task)
        {
            Level[] levels = LevelInfo.Loaded.Items;
            foreach (Level level in activeLevels)
            {
                Update(level, (MapAnimation)level.Extras["MapAnimation"]);
            }
        }

        // Handles animation on a single map
        static void Update(Level level, MapAnimation mapAnimation)
        {
            if (mapAnimation.bRunning)
            {
                List<Player> players = level.getPlayers();  // TODO: This is inefficient, should cache it somewhere

                foreach (AnimBlock animBlock in mapAnimation._blocks)
                {
                    BlockID prevBlock = animBlock._currentBlock;    // Previous frame's block
                    BlockID currentBlock = GetCurrentBlock(animBlock._loopList, mapAnimation._currentTick); // Current frame's block

                    if (currentBlock == prevBlock) // Don't have to send block changes if the block doesn't change
                    {
                        return;
                    }

                    foreach (Player pl in players)
                    {
                        if (pl.Extras.Contains("ShowAnim") && !(bool)pl.Extras["ShowAnim"]) // If the player has reveal animations turned on (animations as red blocks)
                        {
                            continue;
                        }
                        
                        if (currentBlock == UInt16.MaxValue)   // Loop is in off state
                        {
                            pl.RevertBlock(animBlock._x, animBlock._y, animBlock._z);
                        } else  // Loop is in on state
                        {
                            pl.SendBlockchange(currentBlock, animBlock._x, animBlock._y, animBlock._z);
                        }
                    }
                    animBlock.setCurrentBlock(currentBlock);
                }
                mapAnimation._currentTick += 1;
            }
        }

        // Gets the currently visible block given the tick across an array of loops. Returns 65535 if no block is visible at that frame
        private static BlockID GetCurrentBlock(SortedList<ushort, AnimLoop> loops, int tick)
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
        private static BlockID CurrentBlock(AnimLoop loop, int tick)
        {
            BlockID id = System.UInt16.MaxValue;
            if (tick > loop._endTick)
            {
                if (((loop._endTick + 1 - loop._startTick) % loop._stride) < loop._width)       // If the block is active during the loop  TODO: See if itshould say tick +1 or just tick
                {
                    return loop._block;
                }
                return id;
            }

            if (((tick + 1 - loop._startTick) % loop._stride) < loop._width)       // If the block is active during the loop  TODO: See if itshould say tick +1 or just tick
            {
                return loop._block;
            }
            return id;
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
            // The player needs to be whitelisted to use the command
            if (!p.level.BuildAccess.Whitelisted.Contains(p.name))
            {
                p.Message("You do not have permissions to use animations on this level.");
                return;
            }

            string[] args = message.SplitSpaces();

            AnimationArgs animArgs = new AnimationArgs();

            /******************
             * COMMAND PARSER *
             ******************/
            switch (args.Length)
            {
                case 1: // "/anim stop", "/anim start", "/anim show", "/anim delete", "/anim save" (and just "/anim" is also considered length 1)
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
                        ShowAnim(p.level, p);
                        return;
                    }
                    else if (args[0] == "save")     // "/anim save"
                    {
                        AnimationsPlugin.WriteAnimation(p.level);
                        return;
                    }
                    else if (args[0] == "delete")   // "anim delete"
                    {
                        p.Message("Mark where you want to delete animations");

                        animArgs._commandCode = (ushort)AnimCommandCode.delete;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "info")
                    {
                        p.Message("Mark the block you want info of");

                        animArgs._commandCode = (ushort)AnimCommandCode.Info;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 2: // "/anim [stride] [width]", "/anim [delete] [num]"
                    ushort stride, width, num;
                    if (ushort.TryParse(args[0], out stride) && ushort.TryParse(args[1], out width))    // "/anim [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.strideWidth;
                        animArgs._endTick = ushort.MaxValue;
                        animArgs._stride = stride;
                        animArgs._startTick = 0;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "delete" && ushort.TryParse(args[1], out num))
                    {                   // "/anim delete [num]"
                        p.Message("Mark where you want to delete your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.deleteNum;
                        animArgs._idx = num;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 3: // "/anim swap [num1] [num2]", "/anim add stride width"
                    ushort idx1, idx2;
                    if (args[0] == "swap" && ushort.TryParse(args[1], out idx1) && ushort.TryParse(args[2], out idx2))   // "/anim swap [num1] [num2]"
                    {
                        p.Message("Mark where you want to perform the swap");

                        animArgs._commandCode = (ushort)AnimCommandCode.swapNum1Num2;
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

                        animArgs._commandCode = (ushort)AnimCommandCode.addStrideWidth;
                        animArgs._stride = stride;
                        animArgs._width = width;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 4: // "/anim [start] [end] [stride] [width]", "/anim add [num] [stride] [width]"
                    short start; ushort end;
                    if (short.TryParse(args[0], out start) && ushort.TryParse(args[1], out end) && ushort.TryParse(args[2], out stride) && ushort.TryParse(args[3], out width)) // "/anim [start] [end] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        }
                        else if (start > end)
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

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else if (args[0] == "add" && ushort.TryParse(args[1], out num) && ushort.TryParse(args[2], out stride) && ushort.TryParse(args[3], out width)) // "/anim add [num] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
                            return;
                        }

                        p.Message("Mark where you want to place your animation");

                        animArgs._commandCode = (ushort)AnimCommandCode.AddNumStrideWidth;
                        animArgs._idx = num;
                        animArgs._stride = stride;
                        animArgs._width = width;

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
                        else if (start > end)
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

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 6: // "/anim num [start] [end] [stride] [width]"
                    if (args[0] == "num" && short.TryParse(args[1], out start) && ushort.TryParse(args[2], out end) && ushort.TryParse(args[3], out stride) && ushort.TryParse(args[4], out width)) // "/anim num [start] [end] [stride] [width]"
                    {
                        if (width > stride)
                        {
                            p.Message("Width cannot be greater than stride!");
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

        // Information needed when we mark a block. bPlace tells us whether to place or remove, bInfo whether we are
        // looking just for info, bAll whether we look for a specific loop or whether we edit all loops in animation block
        public enum AnimCommandCode : ushort   // Stores info about which command we're using
        {
            delete = 0,
            strideWidth = 1,
            deleteNum = 2,
            swapNum1Num2 = 3,
            addStrideWidth = 4,
            StartEndStrideWidth = 5,
            AddNumStrideWidth = 6,
            AddStartEndStrideWidth = 7,
            AddNumStartEndStrideWidth = 8,
            Info = 9
        }
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

        // Once a block is placed this is called. See the definition of AnimationArgs for the members of the state
        private bool PlacedMark(Player p, Vec3S32[] marks, object state, BlockID block)
        {
            ushort x = (ushort)marks[0].X, y = (ushort)marks[0].Y, z = (ushort)marks[0].Z;
            AnimationArgs animArgs = (AnimationArgs)state;

            switch (animArgs._commandCode)
            {
                case (ushort)AnimCommandCode.delete:
                    DeleteAnimation(p, x, y, z, animArgs._idx, true);
                    break;
                case (ushort)AnimCommandCode.strideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, true);
                    break;
                case (ushort)AnimCommandCode.deleteNum:
                    DeleteAnimation(p, x, y, z, animArgs._idx, false);
                    break;
                case (ushort)AnimCommandCode.swapNum1Num2:
                    SwapAnimation(p, x, y, z, animArgs);
                    break;
                case (ushort)AnimCommandCode.addStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, false);
                    break;
                case (ushort)AnimCommandCode.StartEndStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, true);
                    break;
                case (ushort)AnimCommandCode.AddNumStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, false);
                    break;
                case (ushort)AnimCommandCode.AddStartEndStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, false);
                    break;
                case (ushort)AnimCommandCode.AddNumStartEndStrideWidth:
                    PlaceAnimation(p, x, y, z, animArgs, false);
                    break;
                case (ushort)AnimCommandCode.Info:
                    InfoAnim(p, x, y, z);
                    break;
                default:
                    break;
            }
            return true;
        }

        // Deletes an animation block at (x, y, z). If all is true, then we delete all animations at that position. Else delete that of idx
        private void DeleteAnimation(Player p, ushort x, ushort y, ushort z, ushort idx, bool all)
        {
            if (!p.level.Extras.Contains("MapAnimation")) return;
            MapAnimation mapAnimation = (MapAnimation)p.level.Extras["MapAnimation"];

            // Looks like a massive loop but takes no more than O(number of animation blocks) operations
            foreach (AnimBlock animBlock in mapAnimation._blocks)
            {
                if (!(animBlock._x == x && animBlock._y == y && animBlock._z == z)) { continue; }

                if (all)    // Remove all loops at the location
                {
                    mapAnimation.numAnimations -= (ushort)mapAnimation._blocks.Count;
                    mapAnimation._blocks.Remove(animBlock);
                    p.Message(String.Format("Deleted animation block at ({0}, {1}, {2}}", x, y, z));
                    return;
                }
                else
                {
                    foreach (var kvp in animBlock._loopList)    // Find the loop with the specified index and remove only that
                    {
                        if (kvp.Key == idx)
                        {
                            animBlock._loopList.Remove(kvp.Key);
                            mapAnimation.numAnimations -= 1;
                            p.Message(String.Format("Deleted animation block at ({0}, {1}, {2}}", x, y, z));
                            return;
                        }
                    }
                }
            }
        }

        // Places an animation block at (x, y, z)
        private void PlaceAnimation(Player p, ushort x, ushort y, ushort z, AnimationArgs animArgs, bool all)
        {
            MapAnimation mapAnimation;
            if (!p.level.Extras.Contains("MapAnimation"))
            {
                p.level.Extras["MapAnimation"] = new MapAnimation();
                mapAnimation = (MapAnimation)p.level.Extras["MapAnimation"];
                mapAnimation._blocks = new List<AnimBlock>();
                mapAnimation._currentTick = 0;
                mapAnimation.numAnimations = 0;
                mapAnimation.bRunning = true;
            }

            mapAnimation = (MapAnimation)p.level.Extras["MapAnimation"];

            if (all)    // This means we're overwriting all loops, ignoring the need for a specific index
            {
                // In case we're overwritng existing animations
                foreach (AnimBlock animBlock in mapAnimation._blocks)
                {
                    if (animBlock._x == x && animBlock._y == y && animBlock._z == z)
                    {
                        animBlock._loopList.Clear();
                        AnimLoop loop = new AnimLoop();
                        loop._endTick = animArgs._endTick;
                        loop._startTick = animArgs._startTick;
                        loop._endTick = animArgs._endTick;
                        loop._stride = animArgs._stride;
                        loop._width = animArgs._width;
                        animBlock._loopList.Add(animArgs._idx, loop);

                        mapAnimation.numAnimations += 1;
                        p.Message(String.Format("Placed animation block at ({0}, {1}, {2})", x, y, z));
                        return;
                    }
                }
            }
        }

        // Swaps two loops inside an index. IF a loop doesn't exist, it just changes the key in a straightforward fashion
        private void SwapAnimation(Player p, ushort x, ushort y, ushort z, AnimationArgs animArgs)
        {
            ushort idx1 = animArgs._idx;
            ushort idx2 = animArgs._idx2;

            if (!p.level.Extras.Contains("MapAnimation")) return;

            if (idx1 == idx2) return;

            MapAnimation mapAnimation = (MapAnimation)p.level.Extras["MapAnimation"];

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
                        AnimLoop tmp = animBlock._loopList[idx1];
                        animBlock._loopList[idx1] = animBlock._loopList[idx2];
                        animBlock._loopList[idx2] = animBlock._loopList[idx1];
                    }
                }
            }
        }

        /*********************************
         * STOP, START, SHOW, SAVE, INFO *
         *********************************/

        void StopAnim(Level level)
        {
            if (level.Extras.Contains("MapAnimation"))
            {
                MapAnimation mapAnimation = (MapAnimation)level.Extras["MapAnimation"];
                mapAnimation.bRunning = false;
            }
            return;
        }

        void StartAnim(Level level)
        {
            if (level.Extras.Contains("MapAnimation"))
            {
                MapAnimation mapAnimation = (MapAnimation)level.Extras["MapAnimation"];
                mapAnimation.bRunning = true;
            }
            return;
        }

        void ShowAnim(Level level, Player p)
        {
            if (!p.Extras.Contains("ShowAnim"))
            {
                p.Extras["ShowAnim"] = true;
            }

            if (level.Extras.Contains("MapAnimation"))
            {
                MapAnimation mapAnimation = (MapAnimation)level.Extras["MapAnimation"];
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
            if (level.Extras.Contains("MapAnimation"))
            {
                MapAnimation mapAnimation = (MapAnimation)level.Extras["MapAnimation"];

                AnimBlock selection;
                selection._loopList = null;

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
                p.Message("Animations in this block: <index> : <stride> <width> <start> <end> <blockID>");
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
                    p.Message("Stops the animation in its current state");
                    p.Message(@"/animation start");
                    p.Message(@"Starts the animation in its current state");
                    p.Message(@"/animation show");
                    p.Message(@"Shows all animated blocks in red");
                    p.Message(@"/animation delete");
                    p.Message(@"Delete an animated block");
                    p.Message(@"/animation info");
                    p.Message(@"Returns all info about the animation block");
                    p.Message(@"/animation save");
                    p.Message(@"Saves the animations on the map (happens automatically every few minutes)");
                    p.Message(@"For advanced multi-layered animations, type /help animation 3");
                    break;
                default:
                    p.Message(@"/animation [start] [end] [stride] [width]");
                    p.Message(@"Adds an animation that begins on the start tick and ends on the end tick.");
                    p.Message(@"/animation [stride] [width]");
                    p.Message(@"Adds an animation that begins immediately and never ends.");
                    p.Message(@"NOTE: a tick is 1/10th of a second)");
                    p.Message(@"For additional information, type /help animation 2");
                    break;
            }
        }
    }
}

