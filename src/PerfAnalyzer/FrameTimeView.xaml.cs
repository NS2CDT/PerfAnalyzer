using System;
using System.Collections.Generic;
using System.ComponentModel;
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
  /// Interaction logic for FrameTimeView.xaml
  /// </summary>
  public partial class FrameTimeView : UserControl {
    public FrameTimeView() {
      InitializeComponent();
      NodeList.Sorting += NodeList_Sorting;
    }

    private void NodeList_Sorting(object sender, DataGridSortingEventArgs e) {

      // Default to descending sort direction
      if (e.Column.SortDirection == null) {
        e.Column.SortDirection = ListSortDirection.Ascending;
      }
      e.Handled = false;
    }
  }
}
