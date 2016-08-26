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

    public CallRecord GetNodeWithId(int id) {
      int index = GetNodeIndexWithId(id);

      return index != -1 ? Frame.Calls[index] : default(CallRecord);
    }

    public string NodesString {
      get {
        return Frame.GetNodeString(StartIndex, NodeCount != -1 ? StartIndex + NodeCount : Frame.Calls.Length);
      }
    }

    public override string ToString() {
      return $"Thread({Name}) Time: {TimeMs:F3}ms Nodes: {NodeCount}";
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
