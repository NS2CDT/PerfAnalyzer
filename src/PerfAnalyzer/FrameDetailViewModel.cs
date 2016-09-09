using Caliburn.Micro;
using ICSharpCode.TreeView;
using PerformanceLog;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfAnalyzer {

  public class CallNodeModel : SharpTreeNode {
    public new CallNodeModel Parent { get; set; }
    public PerfNodeStats CombinedStats { get; set; }

    public int Id => CombinedStats.Id;
    public string Name => CombinedStats.Name;
    public double InclTime { get; set; }
    public double ExclTime { get; set; }
    public double Calls { get; set; }
  }

  [Export(typeof(FrameDetailViewModel))]
  public class FrameDetailViewModel : Screen, IHandle<ProfileLog> {
    [Import]
    public IEventAggregator Events { get; set; }


    public FrameDetailViewModel(ProfileFrame frame) {
      Frame = frame;
    }

    private ProfileFrame _frame;

    public ProfileFrame Frame {
      get { return _frame; }
      set {
        _frame = value;



        NotifyOfPropertyChange();
      }
    }


    public void Handle(ProfileLog message) {
      throw new NotImplementedException();
    }
  }
}
