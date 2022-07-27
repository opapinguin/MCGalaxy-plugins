//reference System.dll

/* Stopwatch plugin
 * Special thanks to Venk's stopwatch plugin, on which this was based
 * 
 * Made for Pascalos' zombie survival server. Derived from Upsurge's stopwatch
 */


using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;
using System.Timers;


namespace MCGalaxy
{
    public class StopwatchPlugin : Plugin
    {
        public static string startSecretCode = "START"; // the starting code (e.g. /stopwatch START)
        public static string stopSecretCode = "STOP";   // the stopping code (e.g. /stopwatch STOP)
        public static int timerAccuracy = 47;   // ticks every 47 milliseconds
        public override string creator { get { return "Opapinguin"; } }
        public override string MCGalaxy_Version { get { return "1.9.1.2"; } }
        public override string name { get { return "Stopwatch"; } }

        public override void Load(bool startup)
        {
            Command.Register(new CmdStopwatch());
        }

        public override void Unload(bool shutdown)
        {
            Command.Unregister(Command.Find("Stopwatch"));
        }
    }

    public sealed class CmdStopwatch : Command2
    {
        public override string name { get { return "Stopwatch"; } }
        public override string shortcut { get { return "Timer"; } }
        public override string type { get { return "other"; } }

        public override void Use(Player p, string message)
        {
            p.lastCMD = "Secret";
            if (message != StopwatchPlugin.startSecretCode && message != StopwatchPlugin.stopSecretCode)
            {
                return;
            }

            if (message == StopwatchPlugin.startSecretCode)
            {
                if (!(p.Extras.GetBoolean("timerOn", false)))
                {
                    p.Extras["timerOn"] = true;
                    p.Extras["stopwatch"] = new Stopwatch(p);
                    ((Stopwatch)p.Extras["stopwatch"]).StartTimer(DateTime.Now);
                }
            }

            if (message == StopwatchPlugin.stopSecretCode)  // TODO: potential bug if you stop the timer before it's started
            {
                if (p.Extras.GetBoolean("timerOn", false))
                {
                    p.Extras["timerOn"] = false;
                    ((Stopwatch)p.Extras["stopwatch"]).StopTimer();
                }
            }
        }

        public override void Help(Player p)
        {
            p.Message("%T/Stopwatch [secretcode] %H - starts the stopwatch");
        }
    }

    public class Stopwatch
    {
        string format = @"mm\:ss\.ff";
        private System.Timers.Timer aTimer;
        DateTime startTime;
        Player p;

        public Stopwatch(Player p)
        {
            this.p = p;
        }

        public class ElapsedEventReceiver : ISynchronizeInvoke
        {
            private Thread m_Thread;
            private BlockingCollection<Message> m_Queue = new BlockingCollection<Message>();

            public ElapsedEventReceiver()
            {
                m_Thread = new Thread(Run);
                m_Thread.Priority = ThreadPriority.Lowest;
                m_Thread.IsBackground = true;
                m_Thread.Start();
            }

            private void Run()
            {
                while (true)
                {
                    Message message = m_Queue.Take();
                    message.Return = message.Method.DynamicInvoke(message.Args);
                    message.Finished.Set();
                }
            }

            public IAsyncResult BeginInvoke(Delegate method, object[] args)
            {
                Message message = new Message();
                message.Method = method;
                message.Args = args;
                m_Queue.Add(message);
                return message;
            }

            public object EndInvoke(IAsyncResult result)
            {
                Message message = result as Message;
                if (message != null)
                {
                    message.Finished.WaitOne();
                    return message.Return;
                }
                throw new ArgumentException("result");
            }

            public object Invoke(Delegate method, object[] args)
            {
                Message message = new Message();
                message.Method = method;
                message.Args = args;
                m_Queue.Add(message);
                message.Finished.WaitOne();
                return message.Return;
            }

            public bool InvokeRequired
            {
                get { return Thread.CurrentThread != m_Thread; }
            }

            private class Message : IAsyncResult
            {
                public Delegate Method;
                public object[] Args;
                public object Return;
                public object State;
                public ManualResetEvent Finished = new ManualResetEvent(false);

                public object AsyncState
                {
                    get { return State; }
                }

                public WaitHandle AsyncWaitHandle
                {
                    get { return Finished; }
                }

                public bool CompletedSynchronously
                {
                    get { return false; }
                }

                public bool IsCompleted
                {
                    get { return Finished.WaitOne(0); }
                }
            }
        }

        public void StartTimer(DateTime startTime)
        {
            ElapsedEventReceiver eventReceiver = new ElapsedEventReceiver();
            this.startTime = startTime;
            aTimer = new System.Timers.Timer(StopwatchPlugin.timerAccuracy);
            aTimer.SynchronizingObject = eventReceiver;
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
            aTimer.Start();
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            TimeSpan interval = e.SignalTime - this.startTime;
            p.SendCpeMessage(CpeMessageType.BottomRight2, String.Format("&eCurrent Time: &c{0}",
                interval.ToString(format)
                .TrimStart('0')
                .TrimStart(':')
                .Replace('.', ':')));
        }

        public void StopTimer()
        {
            if (aTimer != null)
            {
                aTimer.Stop();
                aTimer.Dispose();
            }
            p.SendCpeMessage(CpeMessageType.BottomRight2, "");
        }

        ~Stopwatch()
        {
            StopTimer();
        }
    }
}
