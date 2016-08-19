using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoreLinq;

namespace PerformanceLog {

  public enum PSectionId {
    Unspecified,
    Profiler,
    Memory,
    Loading,
    Game,
    Rendering,
    Physics,
    Collision,
    Animation,
    Sound,
    Engine,
    Effects,
    Pathing,
    GC,
  }

  public struct CallRecord {
    public ushort ppid;
    public ushort Depth;
    public uint CallCount;
    public uint Time;
    public uint ExclusiveTime;

    public double TimeMS => Time * 10 / 1000.0;
    public double ExclusiveTimeMS => ExclusiveTime * 10 / 1000.0;

    public bool IsValid() {
      return Depth != ushort.MaxValue;
    }


    public override string ToString() {
      return $"Id: {ppid} Depth: {Depth} CallCount: {CallCount} Time: {TimeMS}";
    }
  }

  public struct CallNode {
    public CallRecord call;
    public uint ExclusiveTime;
    public CallNode[] Children;

    public uint Time => call.Time;
  }

  public struct ProfileSection {
    public uint Time;
    public float Ratio;
  };

  public class NodeStatsDiff {
    public PerfNodeStats Old { get; }
    public PerfNodeStats New { get; }
    public string Name => Old.Name;
    public double Change { get; internal set; }
    public double PeakChange { get; internal set; }

    public NodeStatsDiff(PerfNodeStats old, PerfNodeStats @new) {
      Old = old;
      New = @new;

      Change = GetChangePercent(Old.AvgExclusiveTime, New.AvgExclusiveTime);
      PeakChange = GetChangePercent(Old.MaxAvgExclusiveTime, New.MaxAvgExclusiveTime);
      /*
      TotalTime = TotalTime - other.TotalTime,
        TotalExclusiveTime = TotalExclusiveTime - other.TotalExclusiveTime,
        AvgExclusiveTime = AvgExclusiveTime - other.AvgExclusiveTime,
        MaxAvgExclusiveTime = MaxAvgExclusiveTime - other.MaxAvgExclusiveTime,
        PeakFrameTime = PeakFrameTime - other.PeakFrameTime,

        CallCount = CallCount - other.CallCount,
        AvgCallCount = AvgCallCount - other.AvgCallCount,
        NodeCount = NodeCount - other.NodeCount,
        FrameCount = FrameCount - other.FrameCount,
        */
    }

    private double GetChangePercent(double old, double @new) {
      var change = @new / old;

      if (old == 0.0 || @new == 0.0) {
        return 0;
      }

      if (change < 1.0) {
        // Time decreased
        return -(1.0 - change);
      } else {
        return change - 1;
      }
    }

    public override string ToString() {
      return $"{Name}: {Change*100 :F3}% {Old.AvgExclusiveTime:F3}Ms {New.AvgExclusiveTime:F3}Ms";
    }
  }

  public class BaseNodeStats {
    public string Name { get; protected set; }
    public double TotalTime { get; set; }
    public double TotalExclusiveTime { get; set; }
    public double AvgExclusiveTime { get; set; }
    public double MaxAvgExclusiveTime { get; set; }
    public double PeakFrameTime { get; internal set; }

    public long CallCount { get; set; }
    public double AvgCallCount { get; set; }
    public int NodeCount { get; set; }
    public int FrameCount { get; internal set; }
  }

  public class PerfNodeStats : BaseNodeStats{
    public int Id { get; }
    public ProfileLog Owner { get; }
    public PerfNodeStats Parent { get; set; }

    public PerfNodeStats(ProfileLog owner, string name, int id) {
      Owner = owner;
      Name = name;
      Id = id;
    }

    public void SetStats(long totaExclTime, double peakAvgExclusive, long callCount, int nodeCount) {
      TotalExclusiveTime = ToMs(totaExclTime);
      MaxAvgExclusiveTime = peakAvgExclusive * 10 / 1000.0;
      CallCount = callCount;
      AvgExclusiveTime = TotalExclusiveTime / CallCount;
      NodeCount = nodeCount;
      if (nodeCount != 0) {
        AvgCallCount = CallCount / nodeCount;
      }
    }

    internal void SetStats(ProfileLog.NodeInfo stats) {
      SetStats(stats.TotalExclusiveTime, stats.PeakAvgTime, stats.CallCount, stats.NodeCount);
      FrameCount = stats.FrameCount;
      PeakFrameTime = ToMs(stats.PeakFrameTime);
      TotalTime = ToMs(stats.TotalTime);
    }

    public NodeStatsDiff GetDiff(PerfNodeStats old) {
      return new NodeStatsDiff(old, this);
    }

    private double ToMs(long time) {
      return time * 10 / 1000.0;
    }

    public override string ToString() {
      return $"{Name}({Id}) AvgTime: {AvgExclusiveTime} MaxTime: {MaxAvgExclusiveTime} AvgCall: {AvgCallCount} Calls: {CallCount}";
    }
  }

  public enum PlogEntryType {
    Frame,
    NameId,
    Call,
    CallAndDepthInc,
    CallDepthDec,
    NetworkStats,
  }

  public class ProfileLog {
    public int Version { get; private set; }
    public string FilePath { get; private set; }

    public Dictionary<int, string> ppMap;
    public Dictionary<string, int> ppLookup;
    public string[] Names { get; private set; }
    FastBinaryReader reader;
    double profileCostPerCall;

    public List<ProfileFrame> Frames { get; private set; }
    public List<PerfNodeStats> NodeStats { get; private set; }

    public HashSet<int> IdleNodes { get; private set; }
    public HashSet<int> NetMsgIds { get; private set; }

    public int Threadppid { get; private set; }
    public int ScriptGC_STEP { get; private set; }
    public int HeapFreeId { get; private set; }
    public int HeapAllocateId { get; private set; }
    public int ServerGameUpdateId { get; private set; }
    public int WaitForGCJobId { get; private set; }
    public int WaitForWorldJobId { get; private set; }
    public int WaitForGPUId { get; private set; }
    public object TotalNodes { get; private set; }
    public Dictionary<int, PerfNodeStats> NodeLookup { get; private set; }
    public int ClientGameUpdateId { get; internal set; }

    public ProfileLog() {
      ppMap = new Dictionary<int, string>();
      ppLookup = new Dictionary<string, int>();
      IdleNodes = new HashSet<int>();
    }

    public ProfileLog(string filePath) : this() {
      Load(filePath);
    }

    public List<PerfNodeStats> GetMatchingNodes(string label) {
      var nodeIds = GetMatchNames(label);

      return NodeStats.Where(n => nodeIds.Contains(n.Id)).ToList();
    }

    public HashSet<int> GetMatchNames(string label) {
      return Names.Index().
             Where(p => !NetMsgIds.Contains(p.Key) && p.Value.IndexOf(label, StringComparison.OrdinalIgnoreCase) != -1).
             Select(p => p.Key).
             ToHashSet();
    }

    public int GetNameId(string name) {
      int ppid;
      return ppLookup.TryGetValue(name, out ppid) ? ppid : -1;
    }

    public List<NodeStatsDiff> GetNodeStatsDiff(List<PerfNodeStats> old) {

      var result = new List<NodeStatsDiff>(old.Count);

      foreach (var node in old) {
        var id = GetNameId(node.Name);

        if (id != -1) {
          result.Add(NodeLookup[id].GetDiff(node));
        }
      }

      return result;
    }

    internal void Load(string filePath) {
      FilePath = filePath;
      var logName = Path.GetFileName(filePath);

      using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
        reader = new FastBinaryReader(stream);

        Version = reader.ReadByte();
        profileCostPerCall = readVarInt() * 1E-12;

        using (new Timer("Parse plog: " + logName)) {
          ParseLoop();
        }
      }

      ProcessNameIds();

      using (new Timer("ComputeTimes plog: " + logName)) {
#if true
        Parallel.ForEach(Frames, f => f.ComputeTime());
#else
      foreach (var f in Frames) {
        f.ComputeTime();
      }
#endif
      }

      TotalNodes = Frames.Sum(f => f.Calls.Length);
      NodeStats = GetStatsForRange(0, Frames.Count);
      NodeLookup = NodeStats.ToDictionary(n => n.Id, n => n);

      /*
            cppNames = ppLookup.Keys.Where(k => k.Contains("::")).ToHashSet();

            var times = Frames.Select(f => f.TotalTime).OrderByDescending(i => i).ToArray();

            var maxFrame = Frames.MaxBy(f => f.TotalTime);
           // var avg = Frames.Where(f => f.TotalTime > 180).Average(f => f.TotalTime);
         */


      var threadList = Frames.SelectMany(f => f.Threads).ToList();

      var threads = threadList.GroupBy(t => t.Name).ToLookup(g => g.Key, g => g?.ToList());

      var sgames = GetMatchingNodes("ServerGame");

      var waitNodes = GetMatchingNodes("wait");

      return;
    }

    private void ParseLoop() {
      int depth = 0;
      string name;

      var stream = reader.BaseStream;

      int callNodeCount = 0;
      CallRecord[] calls = new CallRecord[600];

      Frames = new List<ProfileFrame>();
      ProfileFrame lastFrame = null;

      long Length = stream.Length;
      int maxDepth = 0;
      ProfileFrame frame = null;

      while ((int)reader.TotalRead < Length) {
        byte typeByte = reader.ReadByte();
        var type = (PlogEntryType)(typeByte >> 5);


        switch (type) {
          case PlogEntryType.Frame:
            depth = 1;

            if (lastFrame != null) {
              lastFrame.MaxDepth = maxDepth;
              maxDepth = 0;
              lastFrame.SetCalls(calls, callNodeCount);

              var compacted = new CallRecord[callNodeCount];
              lastFrame.Calls = compacted;

              Array.Copy(calls, compacted, callNodeCount);
              callNodeCount = 0;
            }
            frame = ReadFrame(frame);

            Frames.Add(frame);
            lastFrame = frame;
            break;

          case PlogEntryType.NameId:
            int ppId = reader.ReadPPId(typeByte);
            name = readString();
            ppMap.Add(ppId, name);
            break;

          case PlogEntryType.Call:
          case PlogEntryType.CallAndDepthInc:

            calls[callNodeCount].ppid = reader.ReadPPId(typeByte);
            calls[callNodeCount].Depth = (ushort)depth;
            calls[callNodeCount].CallCount = Math.Max(1, readVarInt());
            calls[callNodeCount].Time = readVarInt();
            //calls[callNodeCount].listPosition = (byte)(type == PlogEntyType.CallAndDepthInc ? 0 : 3);
            callNodeCount++;

            if ((callNodeCount + 1) > calls.Length) {
              var newList = new CallRecord[callNodeCount + 200];

              Array.Copy(calls, newList, callNodeCount);
              calls = newList;
            }

            if (type == PlogEntryType.CallAndDepthInc) {
              depth++;
              maxDepth = Math.Max(maxDepth, depth);
            }
            break;

          case PlogEntryType.CallDepthDec:
            if (callNodeCount != 0) {
              // calls[callNodeCount - 1].listPosition = 1;
            }
            //Debug.Assert((depth-1) >= 0);
            depth = Math.Max(depth - 1, 0);
            break;

          case PlogEntryType.NetworkStats:
            ReadNetworkStats();
            break;

          default:
            throw new Exception("Unknown profile section");
        }
      }

      return;
    }

    private void ProcessNameIds() {
      NetMsgIds = new HashSet<int>();
      Names = new string[ppMap.Count];

      foreach (var pair in ppMap) {
        var name = pair.Value;
        var id = pair.Key;
        Names[id] = name;

        if (name.StartsWith("field-", StringComparison.Ordinal) ||
            name.StartsWith("class-", StringComparison.Ordinal) ||
            name.StartsWith("message-", StringComparison.Ordinal) ||
            name.StartsWith("client-", StringComparison.Ordinal)) {

          NetMsgIds.Add(id);
        }

        ppLookup.Add(name, id);

        if (name.Contains("Idle")) {
          IdleNodes.Add(id);
        }
      }

      Threadppid = GetNameId("Thread");
      Debug.Assert(Threadppid != -1);

      ScriptGC_STEP = GetNameId("ScriptGC_STEP");

      HeapFreeId = GetNameId("HeapAllocator::Free");
      HeapAllocateId = GetNameId("HeapAllocator::Allocate");
      ServerGameUpdateId = GetNameId("ServerGame::Update");
      ClientGameUpdateId = GetNameId("ClientGame::Update");

      WaitForGCJobId = GetNameId("ClientGame::FinishRendering (wait for GC job)");
      WaitForWorldJobId = GetNameId("ClientGame::Update (wait for update world job to finish)");

      WaitForGPUId = GetNameId("D3D11Device::Present");
      if (WaitForGPUId == -1) {
        WaitForGPUId = GetNameId("D3D9Device::Present");
      }
      if (WaitForGPUId == -1) {
        WaitForGPUId = GetNameId("OpenGLDevice::Present");
      }
    }

    public struct NodeInfo {
      public long TotalTime;
      public long TotalExclusiveTime;
      public long CallCount;
      public int FrameCount;
      internal int NodeCount;
      internal double PeakAvgTime;
      public uint PeakFrameTime;


      public NodeInfo Combine(NodeInfo b) {
        var result = this;
        result.CallCount += b.CallCount;
        result.TotalTime += b.TotalTime;
        result.TotalExclusiveTime += b.TotalExclusiveTime;
        result.NodeCount += b.NodeCount;
        result.PeakAvgTime = Math.Max(result.PeakAvgTime, b.PeakAvgTime);
        result.PeakFrameTime = Math.Max(result.PeakFrameTime, b.PeakFrameTime);
        return result;
      }
    }

    public List<PerfNodeStats> GetStatsForRange(int start, int end) {

      var list = new NodeInfo[ppLookup.Count];

      var nodeInframe = new byte[ppLookup.Count];
      var parent = new ushort[ppLookup.Count];

      for (int j = start; j < end; j++) {
        var frame = Frames[j];
        var calls = frame.Calls;

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          nodeInframe[id] |= 1;

          list[id].NodeCount++;
          list[id].CallCount += calls[i].CallCount;
          list[id].TotalTime += calls[i].Time;
          list[id].TotalExclusiveTime += exclusiveTime;

          var nodeAvg = exclusiveTime / (double)calls[i].CallCount;
          list[id].PeakAvgTime = Math.Max(nodeAvg, nodeAvg);
          list[id].PeakFrameTime = Math.Max(calls[i].ExclusiveTime, list[id].PeakFrameTime);
        }

        for (int i = 0; i < ppLookup.Count; i++) {
          list[i].FrameCount += nodeInframe[i];
          nodeInframe[i] = 0;
        }
      }

      var nodeStats = new List<PerfNodeStats>(ppLookup.Count - NetMsgIds.Count);

      Debug.Assert(Threadppid == 1);
      //Skip the shared Frame Thread node
      for (int i = 0; i < ppLookup.Count; i++) {
        var name = ppMap[i];

        if (NetMsgIds.Contains(i)) {
          continue;
        }

        var node = new PerfNodeStats(this, name, i);
        node.SetStats(list[i]);
        nodeStats.Add(node);
      }

      return nodeStats;
    }

    public int[] GetNodeTimes(int nodeId, int start = 0, int end = -1) {

      var stats = NodeLookup[nodeId];
      end = end == -1 ? Frames.Count : end;
      var result = new int[end - start];

      for (int j = start; j < end; j++) {
        var calls = Frames[j].Calls;

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          if (id == nodeId) {
            result[j] = (int)calls[i].ExclusiveTime;
            break;
          }
        }
      }

      return result;
    }

    public struct NodeFrameEntry {
      public uint RawTime;
      public int Index;
      public uint CallCount;
      public double Percent;
    }

    public List<NodeFrameEntry> GetNodeFrameStats(int nodeId) {

      var stats = NodeLookup[nodeId];
      var result = new List<NodeFrameEntry>(stats.FrameCount);

      for (int j = 0; j < Frames.Count; j++) {
        var frame = Frames[j];
        var calls = frame.Calls;

        var threadI = 0;
        int threadLimit = frame.Threads.First().EndIndex;

        var curr = new NodeFrameEntry();

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          if (i == threadLimit) {
            threadI++;
            threadLimit = frame.Threads[threadI].EndIndex;
          }

          if (id == nodeId) {
            curr.RawTime += calls[i].ExclusiveTime;
            curr.CallCount += calls[i].CallCount;
            curr.Percent += calls[i].ExclusiveTime / (double)frame.Threads[threadI].Time;
          }
        }
        result.Add(curr);
      }

      return result;
    }

    uint frameCount = 0;
    ulong firstTime = 0;

    private ProfileFrame ReadFrame(ProfileFrame prev) {

      frameCount += 1;
      var endTime = reader.readVarInt64();
      int sectionCount = reader.ReadByte();

      if (firstTime == 0) {
        firstTime = endTime;
      }

      var sections = new ProfileSection[sectionCount];

      for (int i = 0; i < sectionCount; i++) {

        var sectionTime = readVarInt();

        sections[i] = new ProfileSection() {
          Time = sectionTime,
          Ratio = 0,
        };
      }

      var frame = new ProfileFrame(this, frameCount, (long)endTime, sections);

      if (prev != null) {
        frame.SetStartTime(prev.EndTime);
      }

      return frame;
    }

    void ReadNetworkStats() {
      var name = readStringId();

      while (name != "network-end") {
        if (name.StartsWith("client-", StringComparison.Ordinal)) {
          NetworkClientInfo.Read(reader);

        } else if (name.StartsWith("class-", StringComparison.Ordinal)) {
          NetworkClass.Read(this);

        } else if (name.StartsWith("message-", StringComparison.Ordinal)) {
          NetworkMessage.Read(this);
        } else {
          Trace.Fail("Unknown network name " + name);
        }

        name = readStringId();
      }
    }

    private static readonly UTF8Encoding enc = new System.Text.UTF8Encoding();

    private string readString() {
      byte length = reader.ReadByte();
      return reader.ReadString(enc, length);
    }

    internal string readStringId() {
      var id = (int)readVarInt();
      return ppMap[id];
    }

    private ushort readPPid(byte typeByte) {

      unchecked {
        var top = 0;
        var mid = typeByte & 0xF;
        var bot = reader.ReadByte();

        if ((typeByte & 0x10) != 0) {
          top = reader.ReadByte();
        }

        return (ushort)((top << 12) + (mid << 8) + bot);
      }
    }

    internal uint readVarInt() {
      return reader.readVarInt();
    }

    public HashSet<string> cppNames;

    //Returns duration of log in seconds
    public double Duration => ((Frames.Last().EndTimeMS - Frames.First().EndTimeMS) / 1000);

    public override string ToString() {
      return $"{Path.GetFileName(FilePath)} Frames: {Frames.Count} Time: {Duration} seconds";
    }
  }
}
