using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Utilities;

namespace DuplicateRemover
{
    class Program : Form, IDisposable
    {
		[STAThread]
        public static void Main()
        {
			//Debug.WriteLine("Enter the location we want to process: (ex: C:)");
            string target = null;
            bool exists = false;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Program prog = new Program())
            {
				//Seems mono doesn't like Console.ReadLine when working in debugging mode?
				//Time to use the select folder dialog!
				/*new Thread(() =>
                {
                    do
                    {
                        if (!String.IsNullOrEmpty(target))
                            Debug.WriteLine("Invalid or doesn't exist.");
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

                }).Start();*/

                Application.Run(prog);
            }
        }

		//Mono doesn't support console.
		//TODO: Reimplement logging to output.txt or implement better.
		/*static void HookConsoleOutput()
        {
            Stream consoleOut = Console.OpenStandardOutput();
            FileStream log = File.Open(Path.Combine(Environment.CurrentDirectory, "output.txt"), FileMode.Create, FileAccess.Write, FileShare.Read);
            MultiStream dual = new MultiStream(log, consoleOut);
            StreamWriter consoleWriter = new StreamWriter(dual);
            consoleWriter.AutoFlush = true;
            Console.SetOut(consoleWriter);
        }*/

		CheckBox verboseLogging;
		Button selectFolder;
		FolderBrowserDialog folderSelector;
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
			folderSelector = new FolderBrowserDialog ();
			//Could help improve time from open to use?  Nah.
			//folderSelector.RootFolder = Environment.CurrentDirectory;
        }

        public void SetTarget(string dir)
        {
            target = dir;
        }

        public void Run()
        {
            Debug.Write("Building tree...");
            subFiles = IterateFiles(target);
            //subFiles = Directory.GetFiles(Path.GetFullPath(target), "*.*", SearchOption.AllDirectories);
			Debug.WriteLine(" files found", subFiles.Length);
			Debug.WriteLine("Beginning hashing and iterating");
            hashFile = new Dictionary<string, string>();
			//int projectedMemoryUsage = (subFiles.Length * (40 + 256)) + (32 * 16) + 8;
			//Debug.WriteLine("Projected memory usage: {0} bytes ({1} kb, {2} mb) +- a few kb.", projectedMemoryUsage, projectedMemoryUsage / 1024d, projectedMemoryUsage / 1048576d);
            Thread.Sleep(500);
			//Console.Clear();
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
			//Console.CursorTop = 0;
            Stopwatch watch = new Stopwatch();
            Stopwatch totalWatch = new Stopwatch();
            /// HashTimes index, subFiles index, removeTimes index, subFiles length.
            int i = 0, j = 0, c = subFiles.Length;
            for (hashed = 0; hashed < c; ++hashed, ++i)
            {
                //Console.MoveBufferArea(0, 1, Console.WindowWidth, Console.WindowHeight - 2, 0, 0);

                totalWatch.Restart();
                string file = subFiles[hashed];
				if (verboseLogging.Checked)
					Debug.WriteLine (string.Format("Processing: {0}", file));
                if (i == 32)
                    i = 0;
                watch.Start();
                string hash = HashFile(file);
				Debug.WriteLine (string.Format("Hash: {0}", hash));
                watch.Stop();
                if (hash == null)
				{
                    continue;
                }
                hashTimes[i] = watch.ElapsedMilliseconds;

                if (!hashFile.ContainsKey(hash))
                    hashFile.Add(hash, file);
                else
                {
                    if (j == 32)
                        j = 0;
                    watch.Restart();
                    RemoveDuplicate(file);
                    watch.Stop();
                    removeTimes[j++] = watch.ElapsedMilliseconds;
                    ++removed;
                }
                totalWatch.Stop();
                totalTimes[i] = totalWatch.ElapsedMilliseconds;

				if (verboseLogging.Checked) {
					double avgHash = ComputeAverage(hashTimes.values, i),
						avgRemove = ComputeAverage(removeTimes.values, removed),
						avgTotal = ComputeAverage(totalTimes.values, i);
					string avg = string.Format("Avg ms: Hash={0}, Remove={1}, Total={2} Removed={3} Processed={4}/{5}", avgHash, avgRemove, avgTotal, removed, hashed, c);
					Debug.WriteLine (avg);
				}
            }
        }

        public void RemoveDuplicate(string fileName)
        {
            try
            {
				Debug.WriteLine(string.Format("Removing {0}", fileName));
                File.Delete(fileName);
            }
            catch (Exception)
            {
                Console.CursorLeft = 0;
                ///            Processing:
				//Debug.Write("Error     :");
            }
        }

		public double ComputeAverage(double[] times, int count)
        {
            if (count == 0) return Double.NaN;
            int cap = Math.Min(times.Length, count);
			double totalTimes = 0;
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

		void SelectFolderStartProcess(object sender, EventArgs e){
			if (folderSelector.ShowDialog () == DialogResult.OK) {
				Debug.WriteLine (string.Format("Using directory {0}", folderSelector.SelectedPath));
				SetTarget(folderSelector.SelectedPath);
				new Thread (new ThreadStart (Run)).Start ();
			}
		}

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // Program
            // 
            this.ClientSize = new Size(698, 512);
			this.Name = this.Text = "Duplicate Remover";

			verboseLogging = new CheckBox ();
			selectFolder = new Button ();
			selectFolder.Click += SelectFolderStartProcess;
			selectFolder.Text = "Select Folder";
			selectFolder.Location = new Point (5, 5);

			this.Controls.Add (selectFolder);
			selectFolder.Size = selectFolder.PreferredSize;

			verboseLogging.Location = new Point (selectFolder.Right + 5, 5);
			verboseLogging.Text = "Verbose logging";
			verboseLogging.ClientSize = verboseLogging.PreferredSize;
			hashTimes.Location = new Point (5, selectFolder.Bottom + 5);
			this.Controls.Add(hashTimes);
			this.Controls.Add (verboseLogging);

            this.ResumeLayout(false);

			this.ClientSize = this.PreferredSize;

            //this.Controls.Add(CreateChart("Testing", "Testing-X", "Y!", ));
        }
    }
}
