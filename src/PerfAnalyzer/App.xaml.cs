using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PerfAnalyzer {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {

    protected override void OnStartup(StartupEventArgs e) {

      // Check if this was launched by double-clicking a plog.
      if (e.Args != null && e.Args.Count() > 0) {
        Properties["StartupFile"] = e.Args[0];
      }

      base.OnStartup(e);
    }
  }
}
