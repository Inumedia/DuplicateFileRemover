using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Utilities;

namespace DuplicateRemover
{
    class Program : Form, IDisposable
    {
        public static void Main()
        {
            Console.WriteLine("Enter the location we want to process: (ex: C:)");
            string target = null;
            bool exists = false;
            using (Program prog = new Program())
            {
                new Thread(() =>
                {
                    do
                    {
                        if (!String.IsNullOrEmpty(target))
                            Console.WriteLine("Invalid or doesn't exist.");
                        target = Console.ReadLine();
                        if (!target.EndsWith("/") || !target.EndsWith("\\"))
                            target = string.Format("{0}\\", target);
                        try
                        {
                            exists = Directory.Exists(target);
                        }
                        catch (Exception) { }
                    } while (String.IsNullOrEmpty(target) || !exists);
                    HookConsoleOutput();

                    prog.SetTarget(target);
                    prog.Run();

                }).Start();

                Application.Run(prog);
            }
        }

        static void HookConsoleOutput()
        {
            Stream consoleOut = Console.OpenStandardOutput();
            FileStream log = File.Open(Path.Combine(Environment.CurrentDirectory, "output.txt"), FileMode.Create, FileAccess.Write, FileShare.Read);
            MultiStream dual = new MultiStream(log, consoleOut);
            StreamWriter consoleWriter = new StreamWriter(dual);
            consoleWriter.AutoFlush = true;
            Console.SetOut(consoleWriter);
        }

        public string target;
        public string[] subFiles;
        public Dictionary<string, string> hashFile;
        MD5 md5Hasher;

        int removed = 0, hashed = 0;
        MappedChart hashTimes = new MappedChart("hashTimes", "iteration", "Hash Time");
        MappedChart removeTimes = new MappedChart("removeTimes", "iteration", "Removal Time");
        MappedChart totalTimes = new MappedChart("totalTimes", "iteration", "Total Time");

        public Program()
        {
            md5Hasher = MD5.Create();
            InitializeComponent();
        }

        public void SetTarget(string dir)
        {
            target = dir;
        }

        public void Run()
        {
            Console.Write("Building tree...");
            subFiles = IterateFiles(target);
            //subFiles = Directory.GetFiles(Path.GetFullPath(target), "*.*", SearchOption.AllDirectories);
            Console.WriteLine("{0} files found", subFiles.Length);
            Console.Write("Beginning hashing and iterating");
            hashFile = new Dictionary<string, string>();
            int projectedMemoryUsage = (subFiles.Length * (40 + 256)) + (32 * 16) + 8;
            Console.WriteLine("Projected memory usage: {0} bytes ({1} kb, {2} mb) +- a few kb.", projectedMemoryUsage, projectedMemoryUsage / 1024d, projectedMemoryUsage / 1048576d);
            Thread.Sleep(500);
            Console.Clear();
            ProcessFiles();
        }

        public string[] IterateFiles(string currentPath)
        {
            List<string> foundFiles = new List<string>();
            LockFreeQueue<string> subPaths = new LockFreeQueue<string>();

            do
            {
                try
                {
                    string[] subDirectories = Directory.GetDirectories(currentPath);
                    for (int i = 0; i < subDirectories.Length; ++i) subPaths.Push(subDirectories[i]);

                    string[] subFiles = Directory.GetFiles(currentPath);
                    foundFiles.AddRange(subFiles);
                }
                catch (Exception)
                {
                    /// We don't care.
                }
            } while (subPaths.Pop(out currentPath));

            return foundFiles.ToArray();
        }

        public void ProcessFiles()
        {
            Console.CursorTop = 0;
            Stopwatch watch = new Stopwatch();
            Stopwatch totalWatch = new Stopwatch();
            /// HashTimes index, subFiles index, removeTimes index, subFiles length.
            int i = 0, j = 0, c = subFiles.Length;
            for (hashed = 0; hashed < c; ++hashed, ++i)
            {
                //Console.MoveBufferArea(0, 1, Console.WindowWidth, Console.WindowHeight - 2, 0, 0);

                totalWatch.Restart();
                string file = subFiles[hashed];
                //Console.CursorTop = Console.WindowHeight - 2;
                //Console.CursorLeft = 0;
                //string safeFile = string.Format("Processing: {0}", file);
                //safeFile = safeFile.Length > Console.WindowWidth - 2 ? safeFile.Substring(0, Console.WindowWidth - 2) : safeFile;
                //Console.WriteLine(safeFile);
                //Console.CursorTop = Console.WindowHeight - 2;
                if (i == 32)
                    i = 0;
                watch.Start();
                string hash = HashFile(file);
                watch.Stop();
                if (hash == null)
                {
                    //Console.CursorLeft = 0;
                    //Console.Write("Error     :");
                    continue;
                }
                hashTimes[i] = watch.ElapsedMilliseconds;

                if (!hashFile.ContainsKey(hash))
                    hashFile.Add(hash, file);
                else
                {
                    if (j == 32)
                        j = 0;
                    //Console.CursorLeft = 0;
                    //Console.CursorTop = Console.WindowHeight - 2;
                    //Console.Write("Removing  :");
                    watch.Restart();
                    RemoveDuplicate(file);
                    watch.Stop();
                    removeTimes[j++] = watch.ElapsedMilliseconds;
                    ++removed;
                }
                totalWatch.Stop();
                totalTimes[i] = totalWatch.ElapsedMilliseconds;

                //double avgHash = ComputeAverage(hashTimes, k),
                //       avgRemove = ComputeAverage(removeTimes, removed),
                //       avgTotal = ComputeAverage(totalTimes, k);
                //Console.CursorTop = Console.WindowHeight - 1;
                //Console.CursorLeft = 0;
                //string avg = string.Format("Avg ms: Hash={0}, Remove={1}, Total={2} Removed={3} Processed={4}/{5}", avgHash, avgRemove, avgTotal, removed, hashed, c);
                //if (avg.Length < Console.WindowWidth)
                //    avg = string.Join("", avg, new String(' ', Console.WindowWidth - avg.Length));
                //Console.Write(avg);
            }
        }

        public void RemoveDuplicate(string fileName)
        {
            try
            {
                File.Delete(fileName);
            }
            catch (Exception)
            {
                Console.CursorLeft = 0;
                ///            Processing:
                Console.Write("Error     :");
            }
        }

        public double ComputeAverage(long[] times, int count)
        {
            if (count == 0) return Double.NaN;
            int cap = Math.Min(times.Length, count);
            long totalTimes = 0;
            for (int i = 0; i < cap; ++i)
                totalTimes += times[i];
            return totalTimes / (double)cap;
        }

        public string HashFile(string file)
        {
            try
            {
                using (FileStream fs = File.OpenRead(file))
                    return BitConverter.ToString(md5Hasher.ComputeHash(fs));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {

        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // Program
            // 
            this.ClientSize = new Size(698, 512);
            this.Name = "Program";
            this.ResumeLayout(false);

            this.Controls.Add(hashTimes);
            //this.Controls.Add(CreateChart("Testing", "Testing-X", "Y!", ));
        }
    }

    public class MappedChart : Chart
    {
        public double[] values;

        double newMaximum;
        double maximum;
        int valueLength;

        LockFreeQueue<DataPoint> queuedPoints;

        public double this[int i]
        {
            get
            {
                return values[i];
            }
            set
            {
                values[i] = value;
                queuedPoints.Push(new DataPoint(i, value));

                UpdateData();//BeginInvoke(new Action<int, double>(UpdateData), i, value);
            }
        }
        Series mainSeries;
        ChartArea mainArea;
        //Chart target;

        int writing = 0, waiting = 0;
        Stopwatch watch = new Stopwatch();

        public MappedChart(string chartName, string xAxis, string yAxis, int length = 32, Color? primaryColor = null)
        {
            values = new double[length];
            valueLength = length;
            queuedPoints = new LockFreeQueue<DataPoint>();
            CreateChart(chartName, xAxis, yAxis, primaryColor ?? Color.FromArgb(44, 139, 221), length);
        }

        void CreateChart(string chartName, string xaxis, string yaxis, Color primaryColor, int length)
        {
            Name = chartName;
            BackColor = Control.DefaultBackColor;
            Palette = ChartColorPalette.BrightPastel;
            //PaletteCustomColors = 

            mainArea = new ChartArea();
            mainArea.Name = "mainArea";
            mainArea.BackColor = Control.DefaultBackColor;
            ChartAreas.Add(mainArea);

            mainSeries = new Series("mainSeries");
            mainSeries.XValueType = ChartValueType.Int32;
            mainSeries.ChartType = SeriesChartType.Area;
            mainSeries.ChartArea = "mainArea";
            mainSeries.MarkerStyle = MarkerStyle.Circle;
            mainSeries.MarkerSize = 0;
            mainSeries.BorderWidth = 2;
            //srs.Color = ;
            mainSeries.Color = Color.FromArgb((int)(primaryColor.A * .64d), primaryColor.R, primaryColor.G, primaryColor.B);
            mainSeries.MarkerColor = primaryColor;
            mainSeries.BorderColor = primaryColor;
            mainArea.AxisX = new Axis()
            {
                Maximum = length,
                Minimum = 0,
                LabelStyle = new LabelStyle()
                {
                    TruncatedLabels = true
                },
                IntervalOffsetType = DateTimeIntervalType.Number,
                IntervalType = DateTimeIntervalType.Number,
                IsMarksNextToAxis = true,
                IsStartedFromZero = true,
                TextOrientation = TextOrientation.Stacked,
                Name = xaxis
            };
            /*area.AxisY = new Axis()
            {
                MaximumAutoSize = float.NaN
            };*/
            mainArea.AxisY.Maximum = 2000;

            for (int i = 0; i < length; ++i)
                mainSeries.Points.AddXY(i, 0);

            Series.Add(mainSeries);
            //srs.AxisLabel = xaxis;
            //ChartArea area = new ChartArea("mainChart");

            //ChartAreas.Add(
        }

        void UpdateData()
        {
            if (InvokeRequired)
            {
                if (waiting == 1 || Interlocked.CompareExchange(ref writing, 1, 0) == 1) return;

                BeginInvoke(new Action(UpdateData));
                return;
            }

            DataPoint point;
            watch.Restart();
            int count = 0;
            while (queuedPoints.Pop(out point) && watch.ElapsedMilliseconds < 500)
            {
                UpdateData(point);
                ++count;
            }
            this.Invalidate();
            watch.Stop();
            writing = 0;
            if (count != 0)
                waiting = 1;
        }

        protected override void OnPostPaint(ChartPaintEventArgs e)
        {
            waiting = 0;
            UpdateData();
            waiting = 0;
            base.OnPostPaint(e);
        }

        void UpdateData(int index, double value)
        {
            double adjusted = value + (value * 0.05);
            if (index == 0)
                newMaximum = value;
            else
                newMaximum = Math.Max(adjusted, newMaximum);
            if (index == valueLength - 1)
                maximum = Math.Min(maximum, newMaximum);
            else
                maximum = Math.Max(adjusted, maximum);
            mainArea.AxisY.Maximum = maximum;
            mainSeries.Points[index] = new DataPoint(index, value);
        }

        void UpdateData(DataPoint point)
        {
            int index = (int)point.XValue;
            if (point.YValues.Length != 1) return;
            double value = point.YValues[0];
            UpdateData(index, value);
        }
    }
}
