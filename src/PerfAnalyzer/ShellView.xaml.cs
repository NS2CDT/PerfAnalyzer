using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PerfAnalyzer {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class ShellView : Window {
    public ShellView() {
      InitializeComponent();
    }


    public void OpenPickPLog() {
      Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

      dlg.FileName = "TraceLog";
      dlg.DefaultExt = ".plog";
      dlg.Filter = "Trace Log (.plog)|*.plog"; // Filters files by extension

      // Show open file dialog box
      bool? result = dlg.ShowDialog();

      // Process open file dialog box results
      if (result != true) {
        //user didn't pick anything
        return;
      }


      OpenPLog(dlg.FileName);
    }

    private void OpenPLog(string fileName) {
      throw new NotImplementedException();
    }
  }
}
