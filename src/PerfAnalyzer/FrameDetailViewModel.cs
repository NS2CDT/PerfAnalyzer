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
    public FrameDetailViewModel Owner { get; set; }
    public int CallRecordIndex { get; set; }
    public int NodeKey { get; set; }
    public PerfNodeStats MergedStats { get; set; }

    private CallNodeModel(FrameDetailViewModel owner) {
      Owner = owner;

      if (Owner.ShowCheckmarks) {
        IsChecked = false;
      }
    }

    public CallNodeModel(FrameDetailViewModel owner, PerfNodeStats node, CallRecord stats, int nodeIndex)
        : this(owner) {
      MergedStats = node;
      CallRecordIndex = nodeIndex;

      InclTime = stats.TimeMS;
      ExclTime = stats.ExclusiveTimeMS;
      Calls = stats.CallCount;
    }

    public CallNodeModel(FrameDetailViewModel owner, PerfNodeStats node)
        : this(owner) {
      MergedStats = node;
      InclTime = node.AvgInclusiveTime;
      ExclTime = node.AvgExclusiveTime;
      Calls = node.CallCount;
    }

    public CallNodeModel(FrameDetailViewModel owner, PerfNodeStats node, ProfileThread thread, int nodeKey)
        : this(owner) {
      MergedStats = node;
      NodeKey = nodeKey;
      InclTime = thread.TimeMs;
      ExclTime = thread.TimeMs - thread.GetChildTimeMs();
      Calls = 1;

      CallRecordIndex = thread.StartIndex+1;
      LazyLoading = thread.NodeCount != 0;
    }

    public new CallNodeModel Parent => base.Parent as CallNodeModel;
    public int Id => MergedStats.Id;
    public string Name => MergedStats.Name;
    public double InclTime { get; set; }
    public double ExclTime { get; set; }
    public long Calls { get; set; }
    public bool IsThread => base.Parent?.IsRoot == true;
    public double Percent => !IsThread && Parent != null ? (InclTime / (double)Parent.InclTime) * 100 : 0;
    public double ThreadPercent => !IsThread ? (InclTime / (double)GetThread.InclTime) * 100 : 0;
    public double AverageInclusive => MergedStats.AvgInclusiveTime;
    public double AverageExclusive => MergedStats.AvgExclusiveTime;

    public CallNodeModel GetThread => !base.Parent.IsRoot ? Parent.GetThread : this;

    public sealed override bool IsCheckable {
      get {
        return Owner.ShowCheckmarks;
      }
    }

    protected override void LoadChildren() {
      Children.AddRange(Owner.GetChildNodeList(this));
    }

    protected override void OnExpanding() {
      Owner.SetExpanded(NodeKey, true);
    }

    protected override void OnCollapsing() {
      Owner.SetExpanded(NodeKey, false);
    }
  }

  [Export(typeof(FrameDetailViewModel))]
  public class FrameDetailViewModel : Screen, IHandle<ProfileLog> {
    [Import]
    public IEventAggregator Events { get; set; }
    private Dictionary<int, int> parentPairId = new Dictionary<int, int>();
    private List<bool> isExpanded = new List<bool>{true};

    public FrameDetailViewModel(ProfileFrame frame) {
      Frame = frame;
    }

    internal IEnumerable<CallNodeModel> GetChildNodeList(CallNodeModel parent) {

      var nodes = new List<CallNodeModel>();

      foreach (var index in Frame.GetChildNodesIndexs(parent.CallRecordIndex)) {
        int nameId = Frame.Calls[index].ppid;
        int key = GetNodeKey(nameId, parent.NodeKey);

        var child = new CallNodeModel(this, Log.GetNodeStats(nameId), Frame.Calls[index], index) {
          LazyLoading = Frame.NodeHasChildren(index),
          NodeKey = key,
        };

        if (isExpanded[key]) {
          child.IsExpanded = true;
        }
        nodes.Add(child);
      }

      return nodes.OrderByDescending(n => n.InclTime);
    }

    public int GetNodeKey(int typeId, int parentIndex) {
      int id = 0;
      int key = ((typeId << 16) | parentIndex);

      if (!parentPairId.TryGetValue(key, out id)) {
        id = isExpanded.Count;
        parentPairId.Add(key, id);
        isExpanded.Add(false);
      }

      return id;
    }

    private ProfileLog _plog;

    public ProfileLog Log {
      get { return _plog; }
      set {
        _plog = value;
        NotifyOfPropertyChange();
      }
    }

    private ProfileFrame _frame;

    public ProfileFrame Frame {
      get { return _frame; }
      set {
        _frame = value;

        if (Frame.Owner != Log) {
          Log = Frame.Owner;
        }

        if (value != null) {
          CallTree = BuildTreeRoot();
        }

        NotifyOfPropertyChange();
        NotifyOfPropertyChange(nameof(CanGotoPrevFrame));
        NotifyOfPropertyChange(nameof(CanGotoNextFrame));
        NotifyOfPropertyChange(nameof(FrameId));
      }
    }

    private SharpTreeNode BuildTreeRoot() {

      var root = new SharpTreeNode();

      var list = new List<CallNodeModel>();

      foreach (var thread in Frame.Threads) {
        int key = GetNodeKey(thread.NameId, 0);
        var node = new CallNodeModel(this, Log.GetNodeStats(thread.NameId), thread, key);
        node.IsExpanded = isExpanded[key];
        list.Add(node);
      }

      list = list.OrderBy(n => n.Name).ToList();

      list.Sort((CallNodeModel a, CallNodeModel b) => {
        if (a.InclTime == b.InclTime) {
          return 0;
        }
        return a.InclTime < b.InclTime ? 1 : -1;
      });

      root.Children.AddRange(list);

      return root;
    }

    SharpTreeNode _treeroot;

    public SharpTreeNode CallTree {
      get { return _treeroot; }
      set {
        _treeroot = value;
        NotifyOfPropertyChange();
      }
    }

    public string FrameId {
      get { return Frame?.FrameIndex.ToString() ?? ""; }
      set {
        int id = 0;

        if (Log != null && int.TryParse(value, out id)) {

          if (id >= 0 && id < Log.Frames.Count) {
            Frame = Log.Frames[id];
          } else {

          }
        }

      }
    }

    public bool ShowCheckmarks { get; set; } = true;

    public bool CanGotoNextFrame {
      get {
        return Frame?.NextFrame != null;
      }
    }

    public void GotoNextFrame() {
      var next = Frame?.NextFrame;

      if (next != null) {
        Frame = next;
      }
    }

    public bool CanGotoPrevFrame {
      get {
        return Frame?.PrevFrame != null;
      }
    }

    public void GotoPrevFrame() {
      var prev = Frame?.PrevFrame;

      if (prev != null) {
        Frame = prev;
      }
    }


    private void ExpandRecursively(SharpTreeNode node) {

      if (node.Parent != null) {
        if (!node.ShowExpander) {
          return;
        }
        node.IsExpanded = true;
      }

      foreach (var child in node.Children) {
        ExpandRecursively(child);
      }
    }

    public void ExpandAll() {
      ExpandRecursively(CallTree);
    }


    private void CollapseRecursively(SharpTreeNode node) {

      if (!node.IsExpanded) {
        return;
      }

      if (node.Parent != null) {
        node.IsExpanded = false;
      }

      foreach (var child in node.Children) {
        CollapseRecursively(child);
      }
    }

    public void CollapseAll() {
      CollapseRecursively(CallTree);
    }

    public void Handle(ProfileLog message) {
      throw new NotImplementedException();
    }

    internal void SetExpanded(int nodeKey, bool v) {
      isExpanded[nodeKey] = v;
    }


  }
}
