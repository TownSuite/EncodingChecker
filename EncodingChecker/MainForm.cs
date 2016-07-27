using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Ude;

namespace EncodingChecker
{
    public partial class MainForm : Form
    {
        private sealed class WorkerArgs
        {
            internal CurrentAction Action;
            internal string BaseDirectory;
            internal bool IncludeSubdirectories;
            internal string FileMasks;
            internal List<string> ValidCharsets;
        }

        private sealed class WorkerProgress
        {
            internal string FileName;
            internal string DirectoryName;
            internal string Charset;
        }

        private enum CurrentAction
        {
            View,
            Validate,
            Cancel,
        }

        private readonly BackgroundWorker _actionWorker;
        private CurrentAction _currentAction = CurrentAction.Cancel; //Indicates no current action
        private Settings _settings;

        public MainForm()
        {
            InitializeComponent();

            _actionWorker = new BackgroundWorker();
            _actionWorker.WorkerReportsProgress = true;
            _actionWorker.WorkerSupportsCancellation = true;
            _actionWorker.DoWork += ActionWorkerDoWork;
            _actionWorker.ProgressChanged += ActionWorkerProgressChanged;
            _actionWorker.RunWorkerCompleted += ActionWorkerCompleted;
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            //Populate the valid charsets list by using reflection to read the constants in the
            //Ude.Charsets class.
            FieldInfo[] charsetConstants = typeof(Charsets).GetFields(BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public);
            foreach (FieldInfo charsetConstant in charsetConstants)
            {
                if (charsetConstant.FieldType != typeof(string))
                    continue;
                object value = charsetConstant.GetValue(null);
                lstValidCharsets.Items.Add(value);
            }

            //Set the initial action for the action buttons in their Tag properties
            btnView.Tag = CurrentAction.View;
            btnValidate.Tag = CurrentAction.Validate;

            LoadSettings();

            //Size the result list columns based on the initial size of the window
            lstResults.Columns[0].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
            int remainingWidth = lstResults.Width - lstResults.Columns[0].Width;
            lstResults.Columns[1].Width = (30 * remainingWidth) / 100;
            lstResults.Columns[2].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
        }

        private void OnBrowseDirectories(object sender, EventArgs e)
        {
            dlgBrowseDirectories.SelectedPath = txtBaseDirectory.Text;
            if (dlgBrowseDirectories.ShowDialog(this) == DialogResult.OK)
                txtBaseDirectory.Text = dlgBrowseDirectories.SelectedPath;
        }

        private void OnAbout(object sender, EventArgs e)
        {
            using (AboutForm aboutForm = new AboutForm())
                aboutForm.ShowDialog(this);
        }

        #region Loading and saving of settings
        private void LoadSettings()
        {
            string settingsFileName = Path.Combine(Environment.CurrentDirectory, SettingsFileName);
            if (!File.Exists(settingsFileName))
                return;
            using (FileStream settingsFile = new FileStream(settingsFileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                object settingsInstance = formatter.Deserialize(settingsFile);
                _settings = (Settings)settingsInstance;
            }

            txtBaseDirectory.Text = _settings.BaseDirectory;
            chkIncludeSubdirectories.Checked = _settings.IncludeSubdirectories;
            txtFileMasks.Text = _settings.FileMasks;
            if (_settings.WindowPosition != null)
                _settings.WindowPosition.ApplyTo(this);
        }

        private void SaveSettings()
        {
            if (_settings == null)
                _settings = new Settings();
            _settings.BaseDirectory = txtBaseDirectory.Text;
            _settings.IncludeSubdirectories = chkIncludeSubdirectories.Checked;
            _settings.FileMasks = txtFileMasks.Text;
            _settings.WindowPosition = new WindowPosition();
            _settings.WindowPosition.Left = Left;
            _settings.WindowPosition.Top = Top;
            _settings.WindowPosition.Width = Width;
            _settings.WindowPosition.Height = Height;

            string settingsFileName = Path.Combine(Environment.CurrentDirectory, SettingsFileName);
            using (FileStream settingsFile = new FileStream(settingsFileName, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(settingsFile, _settings);
                settingsFile.Flush();
            }
        }

        private const string SettingsFileName = "Settings.bin";
        #endregion

        #region Action button handling
        private void OnAction(object sender, EventArgs e)
        {
            Button actionButton = (Button)sender;
            CurrentAction action = (CurrentAction)actionButton.Tag;
            if (action == CurrentAction.Cancel)
                CancelAction(action);
            else
                StartAction(action);
        }

        private void StartAction(CurrentAction action)
        {
            string directory = txtBaseDirectory.Text;
            if (string.IsNullOrEmpty(directory))
            {
                ShowWarning("Please specify a directory to check");
                return;
            }
            if (!Directory.Exists(directory))
            {
                ShowWarning("The directory you specified '{0}' does not exist", directory);
                return;
            }
            if (action == CurrentAction.Validate && lstValidCharsets.CheckedItems.Count == 0)
            {
                ShowWarning("Select one or more valid character sets to proceed with validation");
                return;
            }

            _currentAction = action;

            Button actionButton = action == CurrentAction.View ? btnView : btnValidate;
            Button otherActionButton = action == CurrentAction.View ? btnValidate : btnView;

            actionButton.Text = CancelCaption;
            actionButton.Tag = CurrentAction.Cancel;
            otherActionButton.Enabled = false;
            actionProgress.Value = 0;
            actionStatus.Text = string.Empty;
            statusBar.Visible = true;
            lstResults.Items.Clear();

            List<string> validCharsets = new List<string>(lstValidCharsets.CheckedItems.Count);
            foreach (string validCharset in lstValidCharsets.CheckedItems)
                validCharsets.Add(validCharset);

            WorkerArgs args = new WorkerArgs();
            args.Action = action;
            args.BaseDirectory = directory;
            args.IncludeSubdirectories = chkIncludeSubdirectories.Checked;
            args.FileMasks = txtFileMasks.Text;
            args.ValidCharsets = validCharsets;
            _actionWorker.RunWorkerAsync(args);
        }

        private void CancelAction(CurrentAction action)
        {
            if (_actionWorker.IsBusy)
            {
                Button actionButton = action == CurrentAction.View ? btnView : btnValidate;
                actionButton.Enabled = false;
                _actionWorker.CancelAsync();
            }
        }
        #endregion

        #region Background worker event handlers and helper methods
        private static void ActionWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            WorkerArgs args = (WorkerArgs)e.Argument;

            string[] allFiles = Directory.GetFiles(args.BaseDirectory, "*.*",
                args.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            IEnumerable<Regex> maskPatterns = GenerateMaskPatterns(args.FileMasks);
            for (int i = 0; i < allFiles.Length; i++)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }

                string path = allFiles[i];
                string fileName = Path.GetFileName(path);
                if (!SatisfiesMaskPatterns(fileName, maskPatterns))
                    continue;

                CharsetDetector detector = new CharsetDetector();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    detector.Feed(fs);
                    detector.DataEnd();
                }
                if (args.Action == CurrentAction.Validate)
                {
                    if (detector.Charset == null)
                        continue;
                    if (args.ValidCharsets.Contains(detector.Charset))
                        continue;
                }

                string directoryName = Path.GetDirectoryName(path);

                int percentageCompleted = (i * 100) / allFiles.Length;
                WorkerProgress progress = new WorkerProgress();
                progress.Charset = detector.Charset ?? "(Unknown)";
                progress.FileName = fileName;
                progress.DirectoryName = directoryName;
                worker.ReportProgress(percentageCompleted, progress);
            }
        }

        private static IEnumerable<Regex> GenerateMaskPatterns(string fileMaskString)
        {
            string[] fileMasks = fileMaskString.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            List<Regex> maskPatterns = new List<Regex>(fileMasks.Length);
            foreach (string fileMask in fileMasks)
            {
                if (string.IsNullOrEmpty(fileMask))
                    continue;
                Regex maskPattern = new Regex("^" + fileMask.Replace(".", "[.]").Replace("*", ".*").Replace("?", ".") + "$",
                    RegexOptions.IgnoreCase);
                maskPatterns.Add(maskPattern);
            }
            return maskPatterns;
        }

        private static bool SatisfiesMaskPatterns(string fileName, IEnumerable<Regex> maskPatterns)
        {
            foreach (Regex maskPattern in maskPatterns)
            {
                if (maskPattern.IsMatch(fileName))
                    return true;
            }
            return false;
        }

        private void ActionWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            WorkerProgress progress = (WorkerProgress)e.UserState;

            ListViewItem resultItem = new ListViewItem(new string[] { progress.Charset, progress.FileName, progress.DirectoryName }, -1);
            lstResults.Items.Add(resultItem);

            actionProgress.Value = e.ProgressPercentage;
            actionStatus.Text = progress.FileName;
        }

        private void ActionWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            foreach (ColumnHeader columnHeader in lstResults.Columns)
                columnHeader.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);

            Button actionButton = _currentAction == CurrentAction.View ? btnView : btnValidate;
            Button otherActionButton = _currentAction == CurrentAction.View ? btnValidate : btnView;

            actionButton.Text = _currentAction == CurrentAction.View ? ViewCaption : ValidateCaption;
            actionButton.Tag = _currentAction;
            otherActionButton.Enabled = true;
            if (e.Cancelled)
                actionButton.Enabled = true;
            statusBar.Visible = false;
        }
        #endregion

        private void ShowWarning(string message, params object[] args)
        {
            MessageBox.Show(this, string.Format(message, args), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private const string ViewCaption = "Vie&w";
        private const string ValidateCaption = "&Validate";
        private const string CancelCaption = "&Cancel";
    }
}