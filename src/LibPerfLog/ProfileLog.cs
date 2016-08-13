using MoreLinq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

  public class PNodeStats {
    public string Name { get; }
    public int Id { get; }
    public ProfileLog Owner { get; }
    public PNodeStats Parent { get; set; }

    public double TotalTime { get; set; }
    public double TotalExclusiveTime { get; set; }
    public double AvgExclusiveTime { get; set; }
    public double MaxAvgExclusiveTime { get; set; }

    public long CallCount { get; set; }
    public double AvgCallCount { get; set; }
    public int NodeCount { get; set; }
    public int FrameCount { get; internal set; }

    public PNodeStats(ProfileLog owner, string name, int id) {
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

    private double ToMs(long time) {
      return time * 10 / 1000.0;
    }

    public override string ToString() {
      return $"{Name}({Id}) AvgTime: {AvgExclusiveTime} MaxTime: {MaxAvgExclusiveTime} AvgCall{AvgCallCount} Calls: {CallCount}";
    }
  }

  public enum PlogEntyType {
    Frame,
    NameId,
    Call,
    CallAndDepthInc,
    CallDepthDec,
    NetworkStats,
  }

  public class ProfileLog {
    public int Version { get; private set; }

    public Dictionary<int, string> ppMap;
    public Dictionary<string, int> ppLookup;
    public string[] Names { get; private set; }
    FastBinaryReader reader;
    double profileCostPerCall;

    public List<ProfileFrame> Frames { get; private set; }
    public List<PNodeStats> NodeStats { get; private set; }

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
    public Dictionary<int, PNodeStats> NodeLookup { get; private set; }

    public ProfileLog() {
      ppMap = new Dictionary<int, string>();
      ppLookup = new Dictionary<string, int>();
      IdleNodes = new HashSet<int>();
    }

    public ProfileLog(string filePath) : this() {
      Load(filePath);
    }

    public List<PNodeStats> GetMatchingNodes(string label) {
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

    internal void Load(string filePath) {
      var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      reader = new FastBinaryReader(stream);

      Version = reader.ReadByte();
      profileCostPerCall = readVarInt() * 1E-12;

      var name = Path.GetFileName(filePath);

      using (new Timer("Parse plog: " + name)) {
        ParseLoop();
      }


      ProcessNameIds();

      using (new Timer("ComputeTimes plog: " + name)) {
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
        var type = (PlogEntyType)(typeByte >> 5);


        switch (type) {
          case PlogEntyType.Frame:
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
            frame = ReadFrame();

            Frames.Add(frame);
            lastFrame = frame;
            break;

          case PlogEntyType.NameId:
            int ppId = reader.ReadPPId(typeByte);
            name = readString();
            ppMap.Add(ppId, name);
            break;

          case PlogEntyType.Call:
          case PlogEntyType.CallAndDepthInc:

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

            maxDepth = Math.Max(maxDepth, depth);

            if (type == PlogEntyType.CallAndDepthInc) {
              depth++;
            }
            break;

          case PlogEntyType.CallDepthDec:
            if (callNodeCount != 0) {
              // calls[callNodeCount - 1].listPosition = 1;
            }
            Debug.Assert((depth - 1) >= 0);
            depth = Math.Max(depth - 1, 0);
            break;

          case PlogEntyType.NetworkStats:
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

      uint ppid;

      ScriptGC_STEP = GetNameId("ScriptGC_STEP");
      Threadppid = GetNameId("Thread");
      Debug.Assert(Threadppid != -1);
      HeapFreeId = GetNameId("HeapAllocator::Free");
      HeapAllocateId = GetNameId("HeapAllocator::Allocate");
      ServerGameUpdateId = GetNameId("ServerGame::Update");

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

    struct NodeInfo {
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
        return result;
      }
    }

    public List<PNodeStats> GetStatsForRange(int start, int end) {

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

      var nodeStats = new List<PNodeStats>(ppLookup.Count - NetMsgIds.Count);

      Debug.Assert(Threadppid == 1);
      //Skip the shared Frame Thread node
      for (int i = 0; i < ppLookup.Count; i++) {
        var name = ppMap[i];

        if (NetMsgIds.Contains(i)) {
          continue;
        }

        var node = new PNodeStats(this, name, i);
        node.SetStats(list[i].TotalExclusiveTime, list[i].PeakAvgTime, list[i].CallCount, list[i].NodeCount);
        node.FrameCount = list[i].FrameCount;
        node.TotalTime = list[i].TotalTime;
        nodeStats.Add(node);
      }

      return nodeStats;
    }

    public struct NodeFrameEntry {
      public uint RawTime;
      public int Index;
      public uint CallCount;
      public double Percent;
    }

    public uint[] GetNodeTimes(int nodeId, int start = 0, int end = -1) {

      var stats = NodeLookup[nodeId];
      end = end == -1 ? Frames.Count : end;
      var result = new uint[end - start];

      for (int j = start; j < end; j++) {
        var calls = Frames[j].Calls;

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          if (id == nodeId) {
            result[j] = calls[i].ExclusiveTime;
            break;
          }
        }
      }

      return result;
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
            curr.RawTime = calls[i].ExclusiveTime;
            curr.CallCount = calls[i].CallCount;
            curr.Percent = calls[i].ExclusiveTime / (double)frame.Threads[threadI].Time;
          }
        }
        result.Add(curr);
      }

      return result;
    }

    uint frameCount = 0;

    private ProfileFrame ReadFrame() {

      frameCount += 1;
      var totalTime = readVarInt() / 1000000.0;
      int sectionCount = reader.ReadByte();

      var sections = new ProfileSection[sectionCount];

      for (int i = 0; i < sectionCount; i++) {

        var sectionTime = readVarInt();

        sections[i] = new ProfileSection() {
          Time = sectionTime,
          Ratio = (float)(sectionTime / totalTime),
        };
      }

      return new ProfileFrame(this, frameCount, totalTime, sections);
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
  }
}
