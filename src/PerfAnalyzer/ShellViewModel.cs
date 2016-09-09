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

namespace PerfAnalyzer {
  [Export(typeof(ShellViewModel))]
  public class ShellViewModel : Conductor<IScreen>.Collection.AllActive, IViewAware {

    private IWindowManager WindowManager;
    private readonly IEventAggregator Events;
    private ShellView View;

    public ObservableMRUList<RecentFile> RecentFiles;

    [ImportingConstructor]
    public ShellViewModel(IWindowManager windowManager, IEventAggregator events) {
      WindowManager = windowManager;
      Events = events;

      RecentFiles = Properties.Settings.Default.RecentLogs;

      var startupTrace = RecentFiles.FirstOrDefault()?.Filepath;

      if (Application.Current.Properties["StartupFile"] != null) {
        startupTrace = (string)Application.Current.Properties["StartupFile"];
      }

      if (File.Exists(startupTrace)) {
        try {
          CurrentPLog = new ProfileLog(startupTrace);
        } catch (Exception e) {
          MessageBox.Show(e.Message + "\n" + e.StackTrace, "Exception while opening ProfileLog");
        }
      }

      ActivateItem(new FrameTimeViewModel(Events));


      //just use an empty ProfileLog if one was not loaded already
      CurrentPLog = CurrentPLog ?? new ProfileLog();
      events.Subscribe(this);
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

    public void OpenProfileLog(string path) {
      ProfileLog tlog;

      try {
        tlog = new ProfileLog(path);
      } catch (Exception e) {

        MessageBox.Show(e.Message + "\n" + e.StackTrace, "Exception while opening ProfileLog");

        return;
      }

      View.RecentLogs.InsertFile(path);

      Properties.Settings.Default.RecentLogs = RecentFiles;
      Properties.Settings.Default.Save();

      CurrentPLog = tlog;
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
  }
}
