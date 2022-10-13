using System;
using System.IO;
using MCGalaxy;
using MCGalaxy.Maths;
using System.Collections.Generic;
using MCGalaxy.Events;
using MCGalaxy.Events.LevelEvents;

using BlockID = System.UInt16;
using MCGalaxy.SQL;
using MCGalaxy.Tasks;

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
        public override string creator { get { return "Opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.4.2"; } }
        public override string name { get { return "Animations"; } }

        public override void Load(bool startup)
        {
            CreateAnimsDir();
            OnLevelLoadedEvent.Register(HandleLevelLoaded,);
            Command.Register(new CmdAnimation());
        }
        public override void Unload(bool shutdown)
        {
            OnLevelLoadedEvent.Unregister(HandleLevelLoaded);
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
                } catch
                {
                    Logger.Log(LogType.Error, "Failed to create Animations folder");
                }
            }
        }

        private void HandleLevelLoaded()
        {

        }
    }

    public static class AnimationScheduler
    {
        const ushort TICKS_PER_SECOND = 10;
        static Scheduler instance;
        static readonly object activateLock = new object();

        public static void Activate()
        {
            lock (activateLock)
            {
                if (instance != null) return;

                instance = new Scheduler("AnimationScheduler");
                instance.QueueRepeat(AnimationsTick, null, TimeSpan.FromMilliseconds(1000 / TICKS_PER_SECOND));
            }
        }

        // Handles animation across all maps
        static void AnimationsTick(SchedulerTask task)
        {
            Level[] levels = LevelInfo.Loaded.Items;
            foreach (Level level in levels)
            {
                if (level.Extras.Contains("MapAnimation"))
                {
                    Update((MapAnimation)level.Extras["MapAnimation"]);
                }
            }
        }

        // Handles animation on a single map
        static void Update(MapAnimation mapAnimation)
        {
            if (mapAnimation.bRunning)
            {
                foreach (AnimBlock animBlock in mapAnimation._blocks)
                {
                    
                }
                mapAnimation._currentTick += 1;
            }
        }

        // Gets the currently visible block given the tick across an array of loops
        private static BlockID GetCurrentBlock(SortedList<ushort, AnimLoop> loops, int tick)
        {
            BlockID loopActiveBlock;
            foreach (var kvp in loops)  // kvp = (index, AnimLoop). Note that this loops from lowest to highest index automatically
            {
                loopActiveBlock = CurrentBlock(kvp.Value, tick);    // The block visible during the current loop
                if (loopActiveBlock != Block.Air) {
                    return loopActiveBlock;
                }
            }

            return Block.Air;
        }

        // Gets the currently visible block for a single loop
        private static BlockID CurrentBlock(AnimLoop loop, int tick)
        {
            BlockID id = Block.Air;
            if (tick > loop._endTick)
            {
                if (((loop._endTick + 1 - loop._startTick) % loop._stride) < loop._width)       // If the block is active during the loop  TODO: See if itshould say tick +1 or just tick
                {
                    return loop._block;
                }
                return id;
            }

            if (((tick+1-loop._startTick) % loop._stride) < loop._width)       // If the block is active during the loop  TODO: See if itshould say tick +1 or just tick
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
                case 1: // "/anim stop", "/anim start", "/anim show", "/anim delete", "/anim save" (and just "/aninm" is also considered length 1)
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
                        SaveAnim(p.level);
                        return;
                    }
                    else if (args[0] == "delete")   // "anim delete"
                    {
                        p.Message("Mark where you want to delete animations");

                        animArgs.bPlace = false;
                        animArgs.bAll = true;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    } else if (args[0] == "info")
                    {
                        p.Message("Mark the block you want info of");

                        animArgs.bInfo = true;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 2: // "/anim [stride] [ID]"
                    BlockID block;
                    ushort stride;
                    if (ushort.TryParse(args[0], out stride) && UInt16.TryParse(args[1], out block))
                    {
                        p.Message("Mark where you want to place your animations");

                        animArgs._block = block;
                        animArgs._endTick = ushort.MaxValue;
                        animArgs._stride = (ushort)Math.Abs(stride);
                        animArgs._startTick = 0;
                        animArgs.bPlace = true;
                        animArgs.bAll = true;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    } else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 3: // "/anim [start tick], [end tick], [stride]"
                    short startTick; ushort endTick;
                    if (short.TryParse(args[0], out startTick) && ushort.TryParse(args[1], out endTick) && ushort.TryParse(args[2], out stride))
                    {
                        p.Message("Mark where you want to place your animations");

                        animArgs._block = Block.Air;
                        animArgs._endTick = endTick;
                        animArgs._stride = (ushort)Math.Abs(stride);
                        animArgs._startTick = startTick;
                        animArgs.bPlace = true;
                        animArgs.bAll = true;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                    break;
                case 4: // "/anim [start tick], [end tick], [stride], [ID]"
                    if (short.TryParse(args[0], out startTick) && ushort.TryParse(args[1], out endTick) && ushort.TryParse(args[2], out stride) && UInt16.TryParse(args[3], out block))
                    {
                        p.Message("Mark where you want to place your animations");

                        animArgs._block = block;
                        animArgs._endTick = endTick;
                        animArgs._stride = (ushort)Math.Abs(stride);
                        animArgs._startTick = startTick;
                        animArgs.bPlace = true;
                        animArgs.bAll = true;

                        p.MakeSelection(1, animArgs, PlacedMark);
                    } else
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
        private struct AnimationArgs
        {
            public BlockID _block;
            public short _startTick;
            public ushort _endTick;
            public ushort _stride;
            public ushort _width;
            public ushort _idx;
            public bool bPlace;
            public bool bInfo;
            public bool bAll;
        }

        // Once a block is placed this is called. See the definition of AnimationArgs for the members of the state
        private bool PlacedMark(Player p, Vec3S32[] marks, object state, BlockID block)
        {
            ushort x = (ushort)marks[0].X, y = (ushort)marks[0].Y, z = (ushort)marks[0].Z;
            AnimationArgs animArgs = (AnimationArgs)state;

            // If we're just looking for information
            if (animArgs.bInfo == true)
            {
                InfoAnim(p, p.level, x, y, z);
            }

            // If we're deleting
            if (animArgs.bPlace == false)
            {
                deleteAnimation(p, x, y, z, animArgs._idx, animArgs.bAll);
            } else
            {
                placeAnimation(p, x, y, z, animArgs, animArgs.bAll);
            }

            return true;
        }

        // Deletes an animation block at (x, y, z). If all is true, then we delete all animations at that position. Else delete that of idx
        private void deleteAnimation(Player p, ushort x, ushort y, ushort z, ushort idx, bool all)
        {
            if (!p.level.Extras.Contains("MapAnimation")) return;   // Nothing to do if there was no map animation to use

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
                } else
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
        private void placeAnimation(Player p, ushort x, ushort y, ushort z, AnimationArgs animArgs, bool all)
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
                        loop._block = animArgs._block;
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

            if (level.Extras.Contains("MapAnimation")) {
                MapAnimation mapAnimation = (MapAnimation)level.Extras["MapAnimation"];
                if ((bool)p.Extras["ShowAnim"])
                {
                    foreach (AnimBlock animBlock in mapAnimation._blocks)
                    {
                        p.SendBlockchange(animBlock._x, animBlock._y, animBlock._z, Block.Red);
                    }
                    p.Extras["ShowAnim"] = false;
                } else
                {
                    p.Extras["ShowAnim"] = true;
                }
            }
            return;
        }

        void SaveAnim(Level level)
        {
            return;
        }

        // Sends the information about a block to the player
        void InfoAnim(Player p, Level level, ushort x, ushort y, ushort z)
        {
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


        /*****************
         * FILE HANDLING *
         *****************/
        
        void ReadAnimation(string level)
        {
            MapAnimation mapAnim;

            mapAnim._currentTick = 0;
            mapAnim.loops = new List<Loop>();
            mapAnim.bRunning = true;

            // TODO: craete extras animation if it does not exist. Then load the map animation into it (Don't have to create actually)

            return;
        }

        // Write the animation thus far to [level]+animation.txt in ./Animations
        void WriteAnimation(string level)
        {
            if (!Level.Load(level).Extras.Contains("MapAnimation")) return;

            MapAnimation mapAnim = (MapAnimation)Level.Load(level).Extras["MapAnimation"];

            ConditionalCreateAnimation(level);

            // TODO: Write the animation to the corresponding file
            return;
        }

        // Creates the animation file [level]+animation.txt in ./Animations if it does not exist
        void ConditionalCreateAnimation(string level)
        {
            if (!AnimationExists(level))
            {
                File.Create(String.Format("Animations/{0}+animation.txt", level));
            }
        }

        // Checks if [level]+animations.txt exists in ./Animations
        bool AnimationExists(string level)
        {
            return File.Exists(String.Format("Animations/{0}+animation.txt", level));
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
            switch(msg)
            {
                case "3":
                    p.Message(@"/animation add [start tick] [end tick] [stride] [width]");
                    p.Message(@"Adds an animation that begins on the start tick and ends on the end tick without overwriting.");
                    p.Message(@"/animation add [num] [start tick] [end tick] [stride] [width]");
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
                    p.Message(@"For advanced multi-layered animations, type /help animation 3");
                    p.Message(@"/animation save");
                    p.Message(@"Saves the animations on the map (happens automatically every few minutes)");
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

