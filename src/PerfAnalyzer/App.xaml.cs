using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PerfAnalyzer {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {

    public static JoinableTaskFactory TaskFactory;

    public App() {
      TaskFactory = new JoinableTaskFactory(new JoinableTaskContext(Current.Dispatcher.Thread, SynchronizationContext.Current));
      InitializeComponent();
    }


    protected override void OnStartup(StartupEventArgs e) {

      // Check if this was launched by double-clicking a plog.
      if (e.Args != null && e.Args.Count() > 0) {
        Properties["StartupFile"] = e.Args[0];
      }

      base.OnStartup(e);
    }
  }
}
