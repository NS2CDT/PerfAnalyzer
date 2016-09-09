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
  /// Interaction logic for FrameDetailView.xaml
  /// </summary>
  public partial class FrameDetailView : UserControl {
    public FrameDetailView() {

      InitializeComponent();

      if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) {
        this.Background = Brushes.Black;
      }
    }
  }
}
