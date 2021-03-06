﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace MoveCute
{
    public partial class MoveCuteForm : Form
    {
        private static readonly int[] SyncDurations =
        {
            60 * 1000,
            30 * 60 * 1000,
            2 * 60 * 60 * 1000,
            8 * 60 * 60 * 1000,
            24 * 60 * 60 * 1000,
            -1
        };

        private static readonly string[] SyncDurationTitles =
        {
            "1 minute",
            "30 minutes",
            "2 hours",
            "8 hours",
            "24 hours",
            "Auto Sync Off",
        };

        private const int LOG_MAX_LENGTH = 3000;
        private const string TIME_FORMAT = "MMM d, HH:mm";

        public MoveCuteForm()
        {
            InitializeComponent();
        }

        private void MoveCuteForm_Load(object sender, EventArgs e)
        {
            LoadFSFile();
        }

        private void MoveCuteForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StoreFSFile();
        }

        public void LogLine(params string[] texts)
        {
            // TODO: avoid scrolling if LogBox has focus
            foreach (string text in texts)
            {
                string timestamp = "[" + DateTime.Now.ToString(TIME_FORMAT) + "] ";
                LogBox.Text += timestamp + text + "\r\n";
            }

            string t = LogBox.Text;
            if (t.Length > LOG_MAX_LENGTH)
            {
                int index = t.IndexOf('\n', t.Length - LOG_MAX_LENGTH);
                LogBox.Text = t.Substring(index + 1);
            }

            LogBox.SelectionLength = LogBox.TextLength;
            LogBox.ScrollToCaret();
        }

        private void SyncList_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool selectionExists = SyncList.SelectedIndex > -1;
            DeleteBtn.Enabled = selectionExists;
            EditBtn.Enabled = selectionExists;
            SyncBtn.Enabled = selectionExists;
        }

        private void DrawSyncListBoxItem(object sender, DrawItemEventArgs e)
        {
            ListBox list = (ListBox)sender;

            if (e.Index < 0) return;
            FileSync fs = (FileSync)list.Items[e.Index];

            e.DrawBackground();
            e.DrawFocusRectangle();

            Brush brush = new SolidBrush(e.ForeColor);
            int textHeight = e.Font.Height;
            
            StringFormat format = new StringFormat();
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags = StringFormatFlags.NoWrap;

            Rectangle rect = new Rectangle();
            rect.Y = e.Bounds.Top;
            rect.Width = e.Bounds.Width/2 - textHeight/2;
            rect.Height = e.Bounds.Height;

            // Left string
            format.Alignment = StringAlignment.Near;
            rect.X = e.Bounds.Left;
            string display = EllipsizeFront(fs.SrcMacro, rect.Width, e);
            e.Graphics.DrawString(display, e.Font, brush, rect, format);

            // Right string
            format.Alignment = StringAlignment.Far;
            rect.X = e.Bounds.Left + e.Bounds.Width/2 + textHeight/2;
            display = EllipsizeFront(fs.DestPath, rect.Width, e);
            e.Graphics.DrawString(display, e.Font, brush, rect, format);

            //arrow
            int cx = e.Bounds.Left + (e.Bounds.Width/2);
            int cy = e.Bounds.Top + (e.Bounds.Height/2);
            int width = textHeight;
            rect = new Rectangle(cx - width/2, cy - textHeight/2, width, textHeight);
            e.Graphics.DrawImage(ArrowPic.Image, rect); //TODO: better way of getting image?
        }

        private string EllipsizeFront(string s, int width, DrawItemEventArgs e)
        {
            if (e.Graphics.MeasureString(s, e.Font).Width <= width) return s;

            s = "..." + s;
            while (e.Graphics.MeasureString(s, e.Font).Width > width)
            {
                s = "..." + s.Substring(4);
            }

            return s;
        }

        private void AddBtn_Clicked(object sender, EventArgs e)
        {
            SyncForm syncForm = new SyncForm();
            syncForm.ShowDialog(); // blocking
            
            FileSync fs = syncForm.FileSync;

            if (string.IsNullOrWhiteSpace(fs.SrcMacro)) return;
            if (string.IsNullOrWhiteSpace(fs.DestPath)) return;
            
            SyncList.Items.Add(fs);

            //StoreFSFile() // probably don't need this here.
        }

        private void EditBtn_Click(object sender, EventArgs e)
        {
            if (SyncList.SelectedItem == null)
            {
                LogLine("Nothing selected to edit.");
                return;
            }
            SyncForm syncForm = new SyncForm((FileSync)SyncList.SelectedItem);
            syncForm.ShowDialog(); // blocking

            //StoreFSFile() // probably don't need this here.
            SyncList.Refresh();
        }

        private void DeleteBtn_Click(object sender, EventArgs e)
        {
            if (SyncList.SelectedItem == null)
            {
                LogLine("Nothing selected to delete.");
                return;
            }

            SyncList.Items.Remove(SyncList.SelectedItem);
            
            if (SyncList.SelectedItem == null) AddBtn.Focus(); // avoid default focus on "Sync All" button
        }

        private void SyncList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (SyncList.IndexFromPoint(e.Location) == ListBox.NoMatches) return;
            EditBtn_Click(sender, e);
        }

        public void CopyFile(FileSync fs, bool surpressLogs = false)
        {
            try
            {
                string srcPath = fs.SrcPath; // calculates from SrcMacro
                string destPath = fs.DestPath;
                if (string.IsNullOrWhiteSpace(srcPath)) throw new Exception($"Macro didn't match anything.");
                if (!File.Exists(srcPath)) throw new Exception($"{srcPath} doesn't exist.");

                if (File.Exists(destPath))
                {
                    if (FilesAreEqual(srcPath, destPath))
                    {
                        LogLine($"{destPath} already up to date.");
                        return;
                    }
                    else File.Delete(destPath);
                }

                File.Copy(srcPath, destPath);
                LogLine($"{srcPath} was copied to {destPath}");
                return;
            }
            catch (Exception ex)
            {
                LogLine($"Failed to copy to {fs.DestPath}:" + ex.Message);
                return;
            }
        }

        private void SyncBtn_Click(object sender, EventArgs e)
        {
            if (SyncList.SelectedItem == null) LogLine("Nothing selected to sync.");
            CopyFile((FileSync)SyncList.SelectedItem);
        }

        private void SyncAllBtn_Click(object sender, EventArgs e)
        {
            if (SyncList.Items.Count == 0)
            {
                LogLine("Nothing to sync.");
                return;
            }

            foreach (FileSync fs in SyncList.Items)
            {
                CopyFile(fs);
            }

            LogLine("Sync Finished.");
        }

        private void SyncTimer_Tick(object sender, EventArgs e)
        {
            LogLine("Starting Auto Sync...");
            SyncAllBtn_Click(sender, e);
            int duration = SyncDurations[FreqTrackBar.Value];
            if (duration < 0) return;
            DateTime nextScheduled = DateTime.Now.AddMilliseconds(duration);
            LogLine("Next Scheduled Sync: " + nextScheduled.ToString(TIME_FORMAT));
        }

        private void FreqTrackBar_Scroll(object sender, EventArgs e)
        {
            //TODO: maybe store value on close

            int idx = FreqTrackBar.Value;
            int duration = SyncDurations[idx];
            string title = SyncDurationTitles[idx];
            FreqValueDisplay.Text = title;

            SyncTimer.Stop();
            if (duration == -1) return;
    
            SyncTimer.Interval = duration;
            SyncTimer.Start();
        }

        private void StoreFSFile()
        {
            var fsArr = SyncList.Items.Cast<FileSync>().ToArray();

            Directory.CreateDirectory("MoveCute_data"); //noop if dir exists
            
            SerializeObject(fsArr, @"MoveCute_data/fs.xml"); // TODO: make const string
        }

        private void LoadFSFile()
        {
            // TODO: probably do this asynchronously
            var fsArr = DeSerializeObject<FileSync[]>(@"MoveCute_data/fs.xml");
            if (fsArr == null)
            {
                LogLine("No previous configuration found.");
                return;
            }

            foreach (FileSync fs in fsArr)
            {
                SyncList.Items.Add(fs);
            }
        }

        // https://stackoverflow.com/questions/1358510
        static bool FilesAreEqual(string srcPath, string destPath)
        {
            const int BYTES_TO_READ = sizeof(long);

            FileInfo first = new FileInfo(srcPath);
            FileInfo second = new FileInfo(destPath);

            if (first.Length != second.Length)
                return false;

            int iterations = (int)Math.Ceiling((double)first.Length / BYTES_TO_READ);

            using (FileStream fs1 = first.OpenRead())
            using (FileStream fs2 = second.OpenRead())
            {
                byte[] one = new byte[BYTES_TO_READ];
                byte[] two = new byte[BYTES_TO_READ];

                for (int i = 0; i < iterations; i++)
                {
                    fs1.Read(one, 0, BYTES_TO_READ);
                    fs2.Read(two, 0, BYTES_TO_READ);

                    if (BitConverter.ToInt64(one, 0) != BitConverter.ToInt64(two, 0))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Serializes an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serializableObject"></param>
        /// <param name="fileName"></param>
        /// https://stackoverflow.com/questions/6115721/
        public void SerializeObject<T>(T serializableObject, string fileName)
        {
            if (serializableObject == null) return;

            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                XmlSerializer serializer = new XmlSerializer(serializableObject.GetType());
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.Serialize(stream, serializableObject);
                    stream.Position = 0;
                    xmlDocument.Load(stream);
                    xmlDocument.Save(fileName);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Deserializes an xml file into an object list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <returns>The deserialized object after reading the file successfully.</returns>
        public T DeSerializeObject<T>(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) { return default(T); }

            T objectOut = default(T);

            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(fileName);
                string xmlString = xmlDocument.OuterXml;

                using (StringReader read = new StringReader(xmlString))
                {
                    Type outType = typeof(T);

                    XmlSerializer serializer = new XmlSerializer(outType);
                    using (XmlReader reader = new XmlTextReader(read))
                    {
                        objectOut = (T)serializer.Deserialize(reader);
                    }
                }
            }
            catch (Exception) { }

            return objectOut;
        }
    }
}
