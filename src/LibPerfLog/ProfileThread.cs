using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceLog {

  public class ProfileThread {
    public int NameId { get; set; }
    public string Name { get; set; }
    public ProfileFrame Frame { get; set; }
    public ProfileThreadFlag Flags { get; set; }

    public uint Time { get; set; }
    public uint IdleTime { get; set; }
    public uint IdleCount { get; private set; }
    public uint GCTime { get; private set; }
    public uint GCCount { get; private set; }

    public int StartIndex { get; set; }
    public int EntryPointIndex { get; set; }
    public int NodeCount { get; internal set; }
    public int EndIndex => StartIndex + NodeCount;

    public ProfileThread(ProfileFrame frame, int nameId, string name) {
      Debug.Assert(name != null);
      Frame = frame;
      Name = name;
      NameId = nameId;
      NodeCount = -1;
    }

    public double TimeMs => Time * 10 / 1000.0;

    public double GetChildTimeMs() {
      uint time = 0;

      foreach (var node in GetChildNodes()) {
        time += node.Time;
      }

      return time * 10 / 1000.0;
    }

    public IEnumerable<CallRecord> Nodes {
      get {
        for (int i = StartIndex; i < StartIndex + NodeCount; i++) {
          yield return Frame.Calls[i];
        }
      }
    }

    public bool ContainsNode(int index) {
      return unchecked((uint)(StartIndex - index)) < NodeCount;
    }

    public int GetNodeIndexWithId(int id) {
      return Frame.GetNodeIndexWithId(id, StartIndex, StartIndex + NodeCount);
    }

    public IEnumerable<CallRecord> GetChildNodes() {
      return Frame.GetChildNodesOfNode(StartIndex);
    }

    public CallRecord GetNodeWithId(int id) {
      int index = GetNodeIndexWithId(id);

      return index != -1 ? Frame.Calls[index] : default(CallRecord);
    }

    public IEnumerable<int> GetNodeParentIndexes(int nodeIndex) {
      return Frame.GetNodeParentIndexes(nodeIndex, StartIndex);
    }

    public string NodesString {
      get {
        return Frame.GetNodeString(StartIndex, NodeCount != -1 ? NodeCount : Frame.Calls.Length);
      }
    }

    public override string ToString() {
      return $"Thread({Name}) Time: {TimeMs:F3}ms Nodes: {NodeCount}";
    }

    public int PeakExclusiveIndex() {
      uint peak = 0;
      int index = -1;
      var nodes = Frame.Calls;
      for (int i = StartIndex; i < StartIndex + NodeCount; i++) {
        if (nodes[i].ExclusiveTime > peak) {
          index = i;
          peak = nodes[i].ExclusiveTime;
        }
      }
      return index;
    }

    public int PeakInclusiveIndex() {
      uint peak = 0;
      int index = -1;
      var nodes = Frame.Calls;
      for (int i = StartIndex; i < StartIndex + NodeCount; i++) {
        if (nodes[i].Time > peak) {
          index = i;
          peak = nodes[i].Time;
        }
      }
      return index;
    }

    internal void AddIdleTime(uint time, uint count) {
      IdleTime += time;
      IdleCount += count;
      Flags |= ProfileThreadFlag.Idle;
    }

    internal void AddGCTime(uint time, uint count) {
      GCTime += time;
      GCCount += count;
      Flags |= ProfileThreadFlag.GCStep;
    }
  }
}
