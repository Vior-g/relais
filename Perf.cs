using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Mémoire et processeur de chaque client Dofus, et « libérer la mémoire » des fenêtres en arrière-plan
    /// (Windows remet de côté la mémoire inutilisée du client ; il la reprend tout seul si besoin).
    /// Aucune interaction avec le jeu lui-même.
    /// </summary>
    public sealed class PerfMonitor
    {
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        const uint PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_SET_QUOTA = 0x0100;

        public sealed class Sample { public double Cpu; public long Ram; }

        readonly App app;
        readonly Dictionary<uint, Sample> samples = new Dictionary<uint, Sample>();
        readonly Dictionary<uint, TimeSpan> lastCpu = new Dictionary<uint, TimeSpan>();
        DateTime lastAt = DateTime.MinValue, lastTrim = DateTime.Now;
        bool busy;
        public event EventHandler Updated;

        public PerfMonitor(App app)
        {
            this.app = app;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 2000;
            t.Tick += delegate { Tick(); };
            t.Start();
        }

        public Sample Of(uint pid)
        {
            Sample s;
            lock (samples) return samples.TryGetValue(pid, out s) ? s : null;
        }

        public long TotalRam
        {
            get { long r = 0; lock (samples) foreach (Sample s in samples.Values) r += s.Ram; return r; }
        }

        void Tick()
        {
            if (busy) return;
            if (!app.S.PerfShow && !app.S.PerfTrim) return;
            List<uint> pids = new List<uint>();
            foreach (GameWindow w in app.Windows) if (!pids.Contains(w.Pid)) pids.Add(w.Pid);
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Measure(pids); } catch { }
                app.Ui.Post(delegate
                {
                    busy = false;
                    if (Updated != null) Updated(this, EventArgs.Empty);
                    if (app.S.PerfTrim && (DateTime.Now - lastTrim).TotalMinutes >= app.S.PerfTrimMinutes) { lastTrim = DateTime.Now; Trim(false); }
                }, null);
            });
        }

        void Measure(List<uint> pids)
        {
            DateTime now = DateTime.UtcNow;
            double elapsed = lastAt == DateTime.MinValue ? 0 : (now - lastAt).TotalMilliseconds;
            lastAt = now;
            Dictionary<uint, Sample> fresh = new Dictionary<uint, Sample>();
            foreach (uint pid in pids)
            {
                try
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        Sample s = new Sample();
                        s.Ram = p.WorkingSet64;
                        TimeSpan cpu = p.TotalProcessorTime;
                        TimeSpan prev;
                        if (elapsed > 0 && lastCpu.TryGetValue(pid, out prev))
                            s.Cpu = Math.Max(0, Math.Min(100, (cpu - prev).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100));
                        lastCpu[pid] = cpu;
                        fresh[pid] = s;
                    }
                }
                catch { }
            }
            lock (samples) { samples.Clear(); foreach (KeyValuePair<uint, Sample> kv in fresh) samples[kv.Key] = kv.Value; }
        }

        /// <summary>Libère la mémoire des clients en arrière-plan (ou de tous). Renvoie les Mo libérés.</summary>
        public void Trim(bool showResult)
        {
            IntPtr fg = Native.GetForegroundWindow();
            List<uint> pids = new List<uint>();
            uint fgPid = 0;
            foreach (GameWindow w in app.Windows) { if (w.Handle == fg) fgPid = w.Pid; }
            foreach (GameWindow w in app.Windows) if (w.Pid != fgPid && !pids.Contains(w.Pid)) pids.Add(w.Pid);
            ThreadPool.QueueUserWorkItem(delegate
            {
                long before = 0, after = 0; int ok = 0;
                foreach (uint pid in pids)
                {
                    try
                    {
                        using (Process p = Process.GetProcessById((int)pid)) before += p.WorkingSet64;
                        IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
                        if (h != IntPtr.Zero) { if (EmptyWorkingSet(h)) ok++; CloseHandle(h); }
                        using (Process p = Process.GetProcessById((int)pid)) after += p.WorkingSet64;
                    }
                    catch { }
                }
                long freed = Math.Max(0, before - after);
                Program.Log("Mémoire libérée : " + (freed / 1048576) + " Mo sur " + ok + " client(s)");
                if (showResult)
                    app.Ui.Post(delegate
                    {
                        if (pids.Count == 0) app.Toast.ShowMessage(L.T("Aucun client en arrière-plan"), false, 2);
                        else if (ok == 0) app.Toast.ShowMessage(L.T("Windows refuse (Dofus lancé en admin ?)"), true, 3);
                        else app.Toast.ShowMessage(Fmt.Bytes(freed) + L.T(" libérés sur ") + ok + L.T(" client(s) en arrière-plan"), false, 3);
                    }, null);
            });
        }
    }
}
