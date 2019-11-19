using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.Composition;
using Caliburn.Micro;
using PerformanceLog;
using System.IO;
using System.Windows;
using System.Threading;
using Ookii.Dialogs.Wpf;
using Microsoft.VisualStudio.Threading;
using System.ComponentModel;

namespace PerfAnalyzer {

  public class ShowFrameDetails {
    public ProfileFrame Frame { get; }

    public ShowFrameDetails(ProfileFrame frame) {
      Frame = frame;
    }
  }

  [Export(typeof(ShellViewModel))]
  public class ShellViewModel : Conductor<IScreen>.Collection.AllActive, IViewAware, IHandle<ShowFrameDetails> {

    private IWindowManager WindowManager;
    private readonly IEventAggregator Events;
    private ShellView View;

    public ObservableMRUList<RecentFile> RecentFiles;
    JoinableTaskFactory TaskFactory;

    [ImportingConstructor]
    public ShellViewModel(IWindowManager windowManager, IEventAggregator events) {
      WindowManager = windowManager;
      Events = events;
      TaskFactory = App.TaskFactory;
      CurrentPLog = new ProfileLog();
      RecentFiles = Properties.Settings.Default.RecentLogs;

      ActivateItem(new FrameTimeViewModel(Events));
      events.Subscribe(this);

      var startupTrace = RecentFiles.FirstOrDefault()?.Filepath;

      if (Application.Current.Properties["StartupFile"] != null) {
        startupTrace = (string)Application.Current.Properties["StartupFile"];
      }

      if (File.Exists(startupTrace)) {
         OpenProfileLog(startupTrace);
      }
    }

    public void ShowFrameDetails(ProfileFrame frame) {
      WindowManager.ShowWindow(new FrameDetailViewModel(frame));
    }

    public override void ActivateItem(IScreen item) {

      if (item is IHandle) {
        Events.Subscribe(item);
      }

      base.ActivateItem(item);
    }

    private ProfileLog _currentProfileLog;

    //automatically notifies other views that a new ProfileLog has been opened using the new ProfileLog message
    public ProfileLog CurrentPLog {
      get {
        return _currentProfileLog;
      }

      set {
        if (value == _currentProfileLog) {
          return;
        }

        _currentProfileLog = value;
        NotifyOfPropertyChange();
        NotifyOfPropertyChange(nameof(Title));

        Events.PublishOnUIThread(value);
      }
    }

    public string Title => CurrentPLog.FilePath;

    private static bool setInitialDirectory = false;

    public void OpenPickProfileLog() {
      Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

      dlg.FileName = "ProfileLog";
      dlg.DefaultExt = ".plog";
      dlg.Filter = "Profile Log (.plog)|*.plog"; // Filters files by extension

      if (!setInitialDirectory) {
        dlg.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Natural Selection 2");
        setInitialDirectory = true;
      }

      // Show open file dialog box
      bool? result = dlg.ShowDialog();

      // Process open file dialog box results
      if (result != true) {
        //user didn't pick anything
        return;
      }

      OpenProfileLog(dlg.FileName);
    }

    readonly AsyncReaderWriterLock loadlock = new AsyncReaderWriterLock();
    volatile CancellationTokenSource loadingToken;
    volatile Task<bool> loadingTask;

    public CancellationTokenSource OpenProfileLog(string path) {
      if (View != null) {
        View.RecentLogs.InsertFile(path);
        Properties.Settings.Default.RecentLogs = RecentFiles;
        Properties.Settings.Default.Save();
      }

      var cts = new CancellationTokenSource();
      Task.Run(async () => {
        using (await loadlock.WriteLockAsync(cts.Token)) {
          if (loadingToken != null && loadingToken.Token.CanBeCanceled) {
            loadingToken.Cancel();
          }
          loadingToken = cts;
          loadingTask = OpenProfileLog(path, loadingToken).ContinueWith(ProfileLogLoadFinished, cts).Unwrap();
        }
      });
      return cts;
    }

    private async Task<ProfileLog> OpenProfileLog(string path, CancellationTokenSource cts) {
      var loadingTask = ProfileLog.OpenAsync(path, cts.Token);
      var progress = CreateLoadProgress(path);
      progress.Show(((Task)loadingTask, cts));

      cts.Token.ThrowIfCancellationRequested();
      return await loadingTask;
    }

    private ProgressDialog CreateLoadProgress(string path) {
      var progress = new ProgressDialog {
        WindowTitle = "Loading " + Path.GetFileName(path),
        Text = "Loading " + path,
        ProgressBarStyle = ProgressBarStyle.MarqueeProgressBar,
        ShowTimeRemaining = false,
        ShowCancelButton = true
      };

      progress.DoWork += new DoWorkEventHandler((o, e) => {
        var progressDialog = (ProgressDialog)o;
        var (task, ct) = ((Task, CancellationTokenSource))e.Argument;
        while (!task.IsCompleted) {
          if (progressDialog.CancellationPending && !ct.IsCancellationRequested) {
            ct.Cancel();
          }
          Thread.Sleep(500);
        }
        return;
      });
      return progress;
    }

    private async Task<bool> ProfileLogLoadFinished(Task<ProfileLog> task, object arg) {
      var cts = (CancellationTokenSource)arg;

      try {
        // Need to be on the mainthread for MessageBox.Show and setting CurrentPLog will
        // trigger notify property events to the GUI system.
        await TaskFactory.SwitchToMainThreadAsync();
        var plog = ((Task<ProfileLog>)task).Result;
        cts.Token.ThrowIfCancellationRequested();

        using (await loadlock.WriteLockAsync()) {
          CurrentPLog = plog;
        }
        return true;
      } catch (AggregateException e) when (e.InnerException is OperationCanceledException) {
        return false;
      } catch (Exception e) {
        MessageBox.Show(e.Message + "\n" + e.StackTrace, "Exception while opening ProfileLog");
        return false;
      } finally {
        using (await loadlock.WriteLockAsync()) {
          cts.Dispose();
          if (cts == loadingToken) {
            loadingToken = null;
            loadingTask = null;
          }
        }
      }
    }

    private void CancelLoad() {
      loadingToken?.Cancel();
    }

    public void CloseCurrentLog() {
      CurrentPLog = new ProfileLog();
    }

    public void AttachView(object view, object context) {
      View = view as ShellView;
      View.RecentLogs.OpenFile += path => OpenProfileLog(path);
    }

    public override object GetView(object context) {
      return null;
    }

    public void FilePreviewDragEnter(DragEventArgs e) {

      if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
        // Note that you can have more than one file.
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);


        if (!files.Any(s => s.EndsWith(".plog", StringComparison.OrdinalIgnoreCase))) {
          e.Effects = DragDropEffects.None;
        }
      }

      e.Handled = true;
    }

    public void FileDropped(DragEventArgs e) {

      if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
        // Note that you can have more than one file.
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

        string logpath = files.FirstOrDefault(s => s.EndsWith(".plog"));

        if (logpath != null) {
          OpenProfileLog(logpath);
        } else {
          e.Effects = DragDropEffects.None;
        }
      }
      e.Handled = true;
    }

    public void Handle(ShowFrameDetails message) {
      ShowFrameDetails(message.Frame);
    }
  }
}
