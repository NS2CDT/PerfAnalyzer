using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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
    public uint ChildTime;

    public uint ExclusiveTime => Time - ChildTime;

    public double TimeMS => Time * 10 / 1000.0;
    public double ExclusiveTimeMS => ExclusiveTime * 10 / 1000.0;


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

  public class ProfileThread {
    public string Name { get; set; }
    public uint Time;
    public ProfileFrame Frame { get; set; }
    public int StartIndex { get; set; }
    public int NodeCount { get; internal set; }

    public ProfileThread(ProfileFrame frame) {
      Frame = frame;
    }
  }

  public class ProfileFrame {
    public double StartTime;
    public double Time;
    public double TotalTime;
    public ProfileSection[] Sections;
    public CallRecord[] Calls;

    public CallNode[] Nodes;
    private uint GCTime;

    public uint FrameIndex { get; private set; }
    public ProfileLog Owner { get; private set; }

    public ProfileFrame(ProfileLog owner, uint frameIndex, double totalTime, ProfileSection[] sections) {
      Owner = owner;
      FrameIndex = frameIndex;
      Time = totalTime;
      Sections = sections;
    }

    internal void SetCalls(CallRecord[] calls, int callNodeCount) {
      var compacted = new CallRecord[callNodeCount];
      Calls = compacted;

      Array.Copy(calls, compacted, callNodeCount);
    }

    public void ComputeTime() {
      if (Calls == null) {
        return;
      }


      int prevDepth = 0, parent = 0;
      ProfileThread curThread = null;
      Threads = new List<ProfileThread>();
      var depthToParent = new int[MaxDepth + 1];

      uint threadppid = Owner.Threadppid;

      for (int i = 1; i < Calls.Length; i++) {
        int depth = Calls[i].Depth;
        var time = Calls[i].Time;
        var callCount = Calls[i].CallCount;
        string name = Owner.ppMap[Calls[i].ppid];

        if (depth == 1) {
          TotalTime += time;
        }

        if (Calls[i].ppid == threadppid) {
          // Debug.Assert(Owner.ppMap[Calls[i + 1].ppid] == "Thread");

          if (depth != 2) {
            Debugger.Break();
          }

          if (curThread != null) {
            curThread.NodeCount = i - curThread.StartIndex;
          }

          curThread = new ProfileThread(this) {
            StartIndex = i,
            Time = Calls[i].Time,
            Name = Owner.ppMap[Calls[i + 1].ppid],
          };
          Threads.Add(curThread);
        } else if (curThread == null) {
          // Skip until we hit a thread because the profiler lost its current profile node context
          continue;
        }

        if (name == "ScriptGC_STEP") {
          //Lift out the GC nodes so they don't distort actual CPU costs when analysing
          GCTime += time;
          GCCount += callCount;

          if (curThread.Name != "CollectGarbageJob::Run") {
            for (int j = i; j >= curThread.StartIndex; j--) {
              Calls[i].Time -= time;
            }
          }
          //subTime += time;
        }

        if (Owner.IdleNodes.Contains(Calls[i].ppid)) {
          TotalTime -= time;
          IdleTime += time;
          IdleCount++;
        }

        if (depth > prevDepth) {
          parent = Math.Max(i - 1, 0);
          Debug.Assert(Calls[parent].Depth == depth - 1);
          depthToParent[depth] = parent;
        } else if (depth < prevDepth) {
          parent = depthToParent[depth];
        } else {
        }

        Calls[parent].ChildTime += time;
        Debug.Assert(parent == 0 || Calls[parent].ExclusiveTime >= 0);

        prevDepth = depth;
      }

      if (curThread != null) {
        curThread.NodeCount = Calls.Length - curThread.StartIndex;
      }
    }

    public string NodesString {
      get {
        var buf = new StringBuilder();

        for (int i = 0; i < Calls.Length; i++) {
          int depth = Calls[i].Depth;
          var time = Calls[i].TimeMS;
          var callCount = Calls[i].CallCount;
          string name = Owner.ppMap[Calls[i].ppid];

          buf.Append(depth);
          buf.Append(' ', depth);
          buf.AppendLine($"{name} Time: {time}/{Calls[i].ExclusiveTimeMS} Calls: {callCount}");
        }

        return buf.ToString();
      }
    }

    public double TotalTimeMS => TotalTime * 1000000;

    public uint GameTime => Sections[(int)PSectionId.Game].Time;
    public double GameRatio => GameTime / TotalTimeMS;

    public uint EngineTime => Sections[(int)PSectionId.Engine].Time;
    public double EngineRatio => EngineTime / TotalTimeMS;

    public uint RenderingTime => Sections[(int)PSectionId.Rendering].Time;
    public double RenderingRatio => RenderingTime / TotalTimeMS;

    public uint IdleTime { get; internal set; }
    public List<ProfileThread> Threads { get; private set; }
    public uint GCCount { get; private set; }
    public int IdleCount { get; private set; }
    public int MaxDepth { get; internal set; }

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
    public Dictionary<uint, string> ppMap;
    public Dictionary<string, uint> ppLookup;
    FastBinaryReader reader;
    double profileCostPerCall;
    byte version;

    public uint ScriptGC_STEP { get; private set; }
    public HashSet<uint> IdleNodes { get; private set; }

    public ProfileLog() {
      ppMap = new Dictionary<uint, string>();
      ppLookup = new Dictionary<string, uint>();
      ScriptGC_STEP = uint.MaxValue;
      IdleNodes = new HashSet<uint>();
    }

    public ProfileLog(string filePath) : this() {
      Load(filePath);
    }

    internal void Load(string filePath) {
      var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      reader = new FastBinaryReader(stream);

      version = reader.ReadByte();
      profileCostPerCall = readVarInt() * 1E-12;

      ParseLoop();
      ProcessNameIds();

      //foreach (var f in Frames) {
      //  f.ComputeTime();
      //}

      //Parallel.ForEach(Frames, f => f.ComputeTime());
      /*
            cppNames = ppLookup.Keys.Where(k => k.Contains("::")).ToHashSet();

            var times = Frames.Select(f => f.TotalTime).OrderByDescending(i => i).ToArray();

            var maxFrame = Frames.MaxBy(f => f.TotalTime);
           // var avg = Frames.Where(f => f.TotalTime > 180).Average(f => f.TotalTime);

            TotalExclusiveTime = new long[ppLookup.Count];
            var counts = new uint[ppLookup.Count];


            foreach (var frame in Frames) {
              frame.ComputeTime();
              var nodes = frame.Calls;

              for (int i = 1; i < nodes?.Length; i++) {
                int id = nodes[i].ppid;
                counts[id] += nodes[i].CallCount;
                TotalExclusiveTime[id] += nodes[i].ExclusiveTime;
              }
            }

            var times2 = TotalExclusiveTime.Index().OrderByDescending(p => p.Value).Select(p => new {
              Name = ppMap[(uint)p.Key],
              Time = p.Value*10 /1000.0,
              Average = (p.Value * 10 / 1000.0) / counts[p.Key],
              Calls = counts[p.Key],
            }).ToArray();

         */
      return;
    }

    private void ProcessNameIds() {
      foreach (var pair in ppMap) {
        var name = pair.Value;
        var id = pair.Key;

        ppLookup.Add(name, id);

        if (name.Contains("Idle")) {
          IdleNodes.Add(id);
        }
      }

      uint ppid;

      if (ppLookup.TryGetValue("ScriptGC_STEP", out ppid)) {
        ScriptGC_STEP = ppid;
      }

      if (ppLookup.TryGetValue("Thread", out ppid)) {
        Threadppid = ppid;
      }
    }

    public long[] TotalExclusiveTime;
    public List<ProfileFrame> Frames { get; private set; }
    public uint Threadppid { get; private set; }

    private void ParseLoop() {
      int depth = 0;
      uint ppId;
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
            ppId = reader.ReadPPId(typeByte);
            name = readString();
            ppMap.Add(ppId, name);
            break;

          case PlogEntyType.Call:
          case PlogEntyType.CallAndDepthInc:

            calls[callNodeCount].ppid = reader.ReadPPId(typeByte);
            calls[callNodeCount].Depth = (ushort)depth;
            calls[callNodeCount].CallCount = readVarInt();
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
        if (name.StartsWith("client-")) {
          NetworkClientInfo.Read(reader);

        } else if (name.StartsWith("class-")) {
          NetworkClass.Read(this);

        } else if (name.StartsWith("message-")) {
          NetworkMessage.Read(this);
        } else {
          System.Diagnostics.Trace.Fail("Unknown network name " + name);
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
      var id = readVarInt();
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
      /*
      unchecked {
        byte v = reader.ReadByte();
        uint result = (v & 0x7Fu);

        int shift = 0;
        while ((v & 0x80u) != 0) {
          v = reader.ReadByte();
          shift += 7;
          result += (v & 0x7Fu) << shift;
        }

        return result;
      }
      */
    }

    string[] sectionNames = {
    "Unspecified",
    "Profiler",
    "Memory",
    "Loading",
    "Game",
    "Rendering",
    "Physics",
    "Collision",
    "Animation",
    "Sound",
    "Engine",
    "Effects",
    "Pathing",
    "GC",
  };
    public HashSet<string> cppNames;
  }
}
