using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoreLinq;
using LinqStatistics;

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

  public enum MarkerKind {
    JOBSTART,
    JOBEND,
    FOCUS_LOST,
    FOCUS_GAINED,

    LUA_STARTID = 128,
    LUA_USER,
    LUA_TRACESFLUSHED,

  }

  public class ProfileMarker {
    public MarkerKind Kind { get; set; }
    public byte ThreadId { get; set; }
    public int UserValue { get; set; }
    public string Label { get; set; }
    public ulong Timestamp { get; set; }
    public ProfileFrame Frame { get; internal set; }

    public override string ToString() {
      return $"Kind: {Kind} Label: {Label} UserValue: {UserValue}";
    }
  }

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
    public double TotalInclusiveTime { get; set; }
    public double AvgInclusiveTime { get; set; }
    public double TotalExclusiveTime { get; set; }
    public double AvgExclusiveTime { get; set; }
    public double MaxAvgExclusiveTime { get; set; }
    public double PeakFrameTime { get; internal set; }
    public double AvgFrameTime { get; internal set; }

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
      AvgFrameTime = ToMs(stats.TotalExclusiveTime / (double)stats.FrameCount);
      AvgInclusiveTime = ToMs(stats.TotalTime / (double)stats.CallCount);
      TotalInclusiveTime = ToMs(stats.TotalTime);
    }

    public NodeStatsDiff GetDiff(PerfNodeStats old) {
      return new NodeStatsDiff(old, this);
    }

    private double ToMs(long time) {
      return time * 10 / 1000.0;
    }

    private double ToMs(double time) {
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
    Extended,
  }

  public class ProfileLog {
    public int Version { get; private set; }
    public string FilePath { get; private set; }

    public Dictionary<int, string> ppMap;
    public Dictionary<string, int> ppLookup;
    public string[] Names { get; private set; }
    FastBinaryReader reader;
    double profileCostPerCall;

    public long StartTime { get; private set; }
    public List<ProfileFrame> Frames { get; private set; }
    public List<PerfNodeStats> NodeStats { get; private set; }
    public Dictionary<int, PerfNodeStats> NodeStatsLookup { get; private set; }

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

    public int ClientGameUpdateId { get; internal set; }

    public ProfileLog() {
      ppMap = new Dictionary<int, string>();
      ppLookup = new Dictionary<string, int>();
      IdleNodes = new HashSet<int>();

      NodeStats = new List<PerfNodeStats>();
      Frames = new List<ProfileFrame>();
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

    public PerfNodeStats GetNodeStats(string name) {
      return NodeStatsLookup[GetNameId(name)];
    }

    public PerfNodeStats GetNodeStats(int ppid) {
      return NodeStatsLookup[ppid];
    }

    public List<NodeStatsDiff> GetNodeStatsDiff(List<PerfNodeStats> old) {

      var result = new List<NodeStatsDiff>(old.Count);

      foreach (var node in old) {
        var id = GetNameId(node.Name);

        if (id != -1) {
          result.Add(NodeStatsLookup[id].GetDiff(node));
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
      NodeStatsLookup = NodeStats.ToDictionary(n => n.Id, n => n);

      var sortedFrames = Frames.
                         OrderByDescending(f => f.MainThread.Time).
                         ToArray();
      var avgtime = sortedFrames.
                    Skip((int)(sortedFrames.Length * 0.1)).
                    Select(f => f.MainThread.TimeMs).
                    Average();
      // Treat any frame that is more than 80% slower than the average of 90 percentile frame time as a slow frame
      SlowFrameThreshold = avgtime * 1.8;

      SlowFrames = Frames.
                   Skip(2). // Skip the first two frames that could be the game still loading in or something
                   Where(f => f.MainThread.TimeMs > SlowFrameThreshold).
                   ToList();

      return;
    }

    public int PrevSlowFrame(int startIndex) {
      if (startIndex == 0 || SlowFrames.Count == 0 || startIndex >= Frames.Count - 1) {
        return -1;
      }
      if (startIndex <= SlowFrames[0].FrameIndex) {
        return -1;
      }
      Debug.Assert(startIndex <= Frames.Count);
      var index = SlowFrames.BinarySearch(startIndex-1, f => f.FrameIndex);
      if (index < 0) {
        index = -index;
      }
      if (SlowFrames[index].FrameIndex == startIndex) {
        index--;
      }
      return index >= 0 ? SlowFrames[index].FrameIndex : -1;
    }

    public int NextSlowFrame(int startIndex) {
      if (SlowFrames.Count == 0 || startIndex >= Frames.Count-1) {
        return -1;
      }
      if (startIndex >= SlowFrames.Last().FrameIndex) {
        return -1;
      }
      Debug.Assert(startIndex <= Frames.Count);
      var index = SlowFrames.BinarySearch(startIndex, f => f.FrameIndex);
      if (index < 0) {
        index = -index;
      }

      return SlowFrames[index + 1].FrameIndex;
    }

    private void ParseLoop() {
      int depth = 0;
      string name;

      var stream = reader.BaseStream;

      int callNodeCount = 0;
      CallRecord[] calls = new CallRecord[600];
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
              CompleteFrame(calls, callNodeCount, maxDepth);
              maxDepth = callNodeCount = 0;
              _markers.Clear();
            }
            frame = ReadFrame(lastFrame);
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

          case PlogEntryType.Extended:
            var id = (ExtendedSection)((reader.ReadByte() << 5) | (typeByte & 0xF));
            ReadExtenedSection(id);
            break;

          default:
            throw new Exception("Unknown profile section");
        }
      }

      CompleteFrame(calls, callNodeCount, maxDepth);

      return;
    }

    private bool skippedFirstFrame = false;
    int frameCount = 0;
    long prevTime = 0;

    private ProfileFrame _currentframe;

    private ProfileFrame ReadFrame(ProfileFrame prev) {

      long endTime = (long)reader.readVarInt64();
      int sectionCount = reader.ReadByte();

      if (StartTime == 0) {
        StartTime = endTime;
      } else {
        endTime = endTime - StartTime;
      }

      var sections = new ProfileSection[sectionCount];

      for (int i = 0; i < sectionCount; i++) {

        var sectionTime = readVarInt();

        sections[i] = new ProfileSection() {
          Time = sectionTime,
          Ratio = 0,
        };
      }

      _currentframe = new ProfileFrame(this, frameCount, endTime, sections);
      frameCount++;

      if (prev != null) {
        _currentframe.SetStartTime(prevTime);
      }
      prevTime = endTime;

      return _currentframe;
    }

    private void CompleteFrame(CallRecord[] calls, int callCount, int maxDepth) {

      _currentframe.SetCalls(calls, callCount);

      // First frame is bogus with no calls
      if (frameCount == 1 && !skippedFirstFrame) {
        frameCount = 0;
        skippedFirstFrame = true;
        prevTime = 0;
        return;
      }

      Frames.Add(_currentframe);

      _currentframe.MaxDepth = maxDepth;


      if (_markers.Count > 0) {
        _currentframe.Markers = _markers.ToArray();
      }
    }

    enum ExtendedSection {
      SectionEnd,
      Markers,
    }

    List<ProfileMarker> _markers = new List<ProfileMarker>();

    private void ReadExtenedSection(ExtendedSection id) {

      if (id == ExtendedSection.Markers) {
        var count = reader.readVarInt();

        for (int i = 0; i < count; i++) {
          var marker = new ProfileMarker();
          marker.Frame = _currentframe;
          marker.Kind = (MarkerKind)reader.readVarInt();
          marker.ThreadId = reader.ReadByte();
          marker.UserValue = (int)reader.readVarInt();
          var labelId = reader.readVarInt();
          marker.Label = labelId != 0 ? ppMap[(int)labelId] : "";
          marker.Timestamp = reader.readVarInt64();

          _markers.Add(marker);
        }
      }
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

    public ListSlice<ProfileFrame> GetFramesInRange(double startMs, double endMs) {
      double epsilon = 1 / 1000.0;
      Debug.Assert(startMs > 0);

      int start = Frames.BinarySearch(startMs, f => f.EndTimeMS, epsilon);
      int end = Frames.BinarySearch(endMs, f => f.EndTimeMS, epsilon);

      start = Math.Abs(start);
      end = Math.Abs(end);

      return new ListSlice<ProfileFrame>(Frames, start, end - start);
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

          nodeInframe[id] = 1;

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

      var stats = NodeStatsLookup[nodeId];
      end = end == -1 ? Frames.Count : end;
      var result = new int[end - start];

      for (int j = start; j < end; j++) {
        var calls = Frames[j].Calls;

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          if (id == nodeId) {
            result[j] += (int)calls[i].ExclusiveTime;
          }
        }
      }

      return result;
    }

    public struct NodeFrameEntry {
      public uint RawTime;
      public ProfileFrame Frame;
      public uint CallCount;
      public byte LastThread;
      public byte ThreadCount;
      public ushort Index;
      public float Percent;
      public ulong InclusiveTotal;

      public double Time => RawTime * 10 / 1000.0;
      public double InclusiveTime => InclusiveTotal * 10 / 1000.0;

      public override string ToString() {
        return $"{Time:F4}Ms Calls: {CallCount} Frame: {Frame}";
      }
    }

    public List<NodeFrameEntry> GetNodeFrameStats(int nodeId, int startFrame = -1, int frameCount = -1, int threadId = -1) {

      var stats = NodeStatsLookup[nodeId];
      var result = new List<NodeFrameEntry>(stats.FrameCount);

      if (startFrame == -1) {
        startFrame = 0;
      }

      if (frameCount == -1) {
        frameCount = Frames.Count;
      } else {
        frameCount += startFrame;
      }

      for (int j = startFrame; j < frameCount; j++) {
        var frame = Frames[j];
        var calls = frame.Calls;

        var threadI = 0;
        var thread = frame.Threads.First();

        //Filter to only nodes that are children of the the thread with the matching name id
        if (threadId != -1) {
          thread = frame.Threads.FirstOrDefault(t => t.NameId == threadId);
          if (thread == null) {
            continue;
          }
        }

        int threadLimit = thread.EndIndex;

        var curr = new NodeFrameEntry();
        bool seen = false;

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          if (i == threadLimit) {
            if (threadId != -1) {
              break;
            }
            threadI++;
            threadLimit = frame.Threads[threadI].EndIndex;
          }

          if (id == nodeId) {
            seen = true;
            curr.Frame = frame;
            // Inclusive
            curr.InclusiveTotal += calls[i].Time;
            curr.RawTime += calls[i].ExclusiveTime;
            curr.CallCount += calls[i].CallCount;
            if (curr.LastThread != threadI) {
              curr.ThreadCount++;
            }
            curr.LastThread = (byte)threadI;
            curr.Percent += (float)(calls[i].ExclusiveTime / (double)frame.Threads[threadI].Time);
          }
        }

        if (seen) {
          result.Add(curr);
        }
      }

      return result;
    }

    public List<NodeFrameEntry[]> GetNodeFrameStats(IList<int> nodeIds) {

      var idLookup = new byte[Names.Length];
      int countId = 1;// Skip zero we default to that slot for non interesting nodes

      foreach (var id in nodeIds) {
        idLookup[id] = (byte)countId++;
      }

      var stats = new NodeFrameEntry[nodeIds.Count][];

      for (int i = 1; i < nodeIds.Count; i++) {
        stats[i] = new NodeFrameEntry[NodeStatsLookup[nodeIds[i - 1]].FrameCount];
      }

      var count = new int[nodeIds.Count];

      for (int j = 0; j < Frames.Count; j++) {
        var frame = Frames[j];
        var calls = frame.Calls;

        var threadI = 0;
        int threadLimit = frame.Threads.First().EndIndex;

        for (int i = 1; i < calls.Length; i++) {
          int id = calls[i].ppid;
          var exclusiveTime = calls[i].ExclusiveTime;

          var slot = idLookup[id];

          if (slot != 0) {
            int index = count[slot];
            var array = stats[slot];
            array[index].InclusiveTotal += calls[i].Time;
            array[index].Frame = frame;
            array[index].RawTime += calls[i].ExclusiveTime;
            array[index].CallCount += calls[i].ExclusiveTime;

            // curr.Percent += (float)(calls[i].ExclusiveTime / (double)frame.Threads[threadI].Time);
          }
        }
      }

      return stats.Skip(1).ToList();
    }


    public HashSet<string> cppNames;

    //Returns duration of log in seconds
    public double Duration => ((Frames.Last().EndTimeMS - Frames.First().EndTimeMS) / 1000);

    public double SlowFrameThreshold { get; private set; } = 10.0;
    public List<ProfileFrame> SlowFrames { get; private set; }

    public override string ToString() {
      return $"{Path.GetFileName(FilePath)} Frames: {Frames.Count} Time: {Duration} seconds";
    }
  }
}
