using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerformanceLog {
  [Flags]
  public enum ProfileThreadFlag {
    GCStep     = 0x1,
    Waiting    = 0x2,
    Idle       = 0x4,
    MainThread = 0x8
  }

  public class ProfileThread {
    public int NameId { get; set; }
    public string Name { get; set; }
    public ProfileThreadFlag Flags { get; set; }

    public uint Time;
    public ProfileFrame Frame { get; set; }
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
      return Frame.GetNodeIndexWithId(id, StartIndex, StartIndex+NodeCount);
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
      return $"Thread({Name}) Time: {TimeMs:3.5}ms Nodes: {NodeCount}";
    }
  }



  public class ProfileFrame {
    public double StartTime;
    public double Time;
    public double TotalTime;
    public ProfileSection[] Sections;
    public CallRecord[] Calls;

    public uint IdleTime { get; internal set; }
    public List<ProfileThread> Threads { get; private set; }
    public uint GCCount { get; private set; }
    public int IdleCount { get; private set; }
    public int MaxDepth { get; internal set; }
    private uint GCTime;

    public uint FrameIndex { get; private set; }
    public ProfileLog Owner { get; private set; }

    public ProfileFrame(ProfileLog owner, uint frameIndex, double totalTime, ProfileSection[] sections) {
      Owner = owner;
      FrameIndex = frameIndex;
      Time = totalTime;
      Sections = sections;
    }

    internal void SetCalls(CallRecord[] calls, int callCount) {
      var compacted = new CallRecord[callCount];
      Calls = compacted;

      Array.Copy(calls, compacted, callCount);
    }

    public void ComputeTime() {
      Threads = new List<ProfileThread>();

      if (Calls == null) {
        Calls = Array.Empty<CallRecord>();
        return;
      }

      int prevDepth = 0, parent = 0;
      ProfileThread thread = null;

      var depthToParent = new int[MaxDepth + 1];

      int threadNodeId = Owner.Threadppid;

      for (int i = 1; i < Calls.Length; i++) {
        int depth = Calls[i].Depth;
        var time = Calls[i].Time;
        var callCount = Calls[i].CallCount;
        string name = Owner.ppMap[Calls[i].ppid];

        if (depth == 1) {
          TotalTime += time;
        }

        if (Calls[i].ppid == threadNodeId) {
          // Set the node count for the previous thread
          if (thread != null) {
            thread.NodeCount = i - thread.StartIndex;
          }

          thread = AddThread(i);
        } else if (thread == null) {
          // Skip until we hit a thread because the profiler lost its current profile node context
          continue;
        }

        Calls[i].ExclusiveTime = time;

        if (depth > prevDepth) {
          // We've entered a child node record the previous node as the parent for this depth
          parent = Math.Max(i - 1, 0);
          Debug.Assert(Calls[parent].Depth == depth - 1);
          depthToParent[depth] = parent;
        } else if (depth < prevDepth) {
          parent = depthToParent[depth];

          if (i > 0 && depth == 0) {
            parent = i;
          }
          Debug.Assert(depth == 0 || depthToParent[depth] < i && Calls[depthToParent[depth]].Depth < depth);
        }

        if (Owner.IdleNodes.Contains(Calls[i].ppid)) {
          TotalTime -= time;
          IdleTime += time;
          IdleCount++;

          SubTime(i, depthToParent);
          thread.Flags |= ProfileThreadFlag.Idle;
        }

        if (name == "ScriptGC_STEP" && thread.Name != "CollectGarbageJob::Run") {
          //Lift out the GC nodes so they don't distort actual CPU costs when analysing
          GCTime += time;
          GCCount += callCount;
          thread.Flags |= ProfileThreadFlag.GCStep;

          /* Subtract time from all the parents of this Node */
          for (int parentI = depth; parentI >= 0; parentI--) {
            var j = depthToParent[parentI];
            int parentTime = (int)Calls[j].Time - (int)time;
            Debug.Assert(j == 0 || parentTime >= -1000);
            Calls[j].Time = (uint)Math.Max(parentTime, 0); ;

            parentTime = (int)Calls[j].ExclusiveTime - (int)time;
            Debug.Assert(j == 0 || parentTime >= -5100);
            Calls[j].ExclusiveTime = (uint)Math.Max(parentTime, 0);
          }
          //subTime += time;
        }

        /* Round*/
        int newTime = (int)Calls[parent].ExclusiveTime - (int)time;
        if (newTime < 0) {
          /*
           * HeapAllocator::Free and HeapAllocator::Free
           * seem to have weird times
           */
          //Debug.Assert(parent == 0 || newTime >= -1);
          newTime = 0;
        }
        Calls[parent].ExclusiveTime = (uint)newTime;

        if (true) {

        }

        Debug.Assert(parent == 0 || parent == thread.StartIndex || (Calls[parent].ExclusiveTime >= 0));

        prevDepth = depth;
      }

      if (thread != null) {
        thread.NodeCount = Calls.Length - thread.StartIndex;
      }
    }

    private void SubTime(int index, int[] depthToParent) {
      var time = Calls[index].ExclusiveTime;
      var startDepth = Calls[index].Depth;

      for (int depth = startDepth; depth >= 0; depth--) {
        var j = depthToParent[depth];
        int parentTime = (int)Calls[j].Time - (int)time;
        Debug.Assert(j == 0 || parentTime >= -1000);
        Calls[j].Time = (uint)Math.Max(parentTime, 0); ;

        if (depth == startDepth) {
          parentTime = (int)Calls[j].ExclusiveTime - (int)time;
          Debug.Assert(j == 0 || parentTime >= -5100);
          Calls[j].ExclusiveTime = (uint)Math.Max(parentTime, 0);
        }
      }
    }

    private ProfileThread AddThread(int start) {
      int entryPoint = start + 1;
      int ownerId = Calls[entryPoint].ppid;
      var depth = Calls[entryPoint].Depth;

      /* Try to find a better node to use for a thread name and entryPoint if we hit HeapAllocator::Free or HeapAllocator::Allocate as the first node */
      if (ownerId == Owner.HeapFreeId || ownerId == Owner.HeapAllocateId) {
        for (int j = start + 1; j < Calls.Length; j++) {

          if (Calls[j].Depth <= depth) {
            break;
          } else if (Calls[j].Depth != depth + 1) {
            /* Reached the end of the nodes belonging to this thread */
            continue;
          }
          int id = Calls[j].ppid;

          if (id != Owner.HeapFreeId && id != Owner.HeapAllocateId) {
            entryPoint = j;
            ownerId = id;
            break;
          }
        }
      }

      var thread = new ProfileThread(this, ownerId, Owner.ppMap[ownerId]) {
        StartIndex = start,
        Time = Calls[start].Time,
        EntryPointIndex = entryPoint,
      };

      if (ownerId == Owner.ServerGameUpdateId) {
        thread.Flags |= ProfileThreadFlag.MainThread;
        MainThread = thread;
      }

      Threads.Add(thread);
      return thread;
    }

    public IEnumerable<CallRecord> GetChildNodesOfNode(int nodeIndex) {
      int minDepth = Calls[nodeIndex].Depth;

      for (int i = nodeIndex+1; i < Calls.Length; i++) {

        if (Calls[i].Depth <= minDepth) {
          yield break;
        }

        if (Calls[i].Depth == minDepth+1) {
          yield return Calls[i];
        }
      }
    }

    public int GetNodeIndexWithId(int id, int searchStart = -1, int searchEnd = -1) {
      if (searchStart == -1) {
        searchStart = 0;
      }

      if (searchEnd == -1) {
        searchEnd = Calls.Length;
      }

      for (int i = searchStart; i < searchEnd; i++) {
        if (Calls[i].ppid == id) {
          return i;
        }
      }

      return -1;
    }

    public CallRecord GetNodeWithId(int id, int start = -1) {
      int index = GetNodeIndexWithId(id, start);

      return index != -1 ? Calls[index] : default(CallRecord);
    }

    public string NodesString {
      get {
        return GetNodeString(0, Calls.Length);
      }
    }

    public string GetNodeString(int start, int count) {
      var buf = new StringBuilder();

      for (int i = start; i < count; i++) {
        int depth = Calls[i].Depth;
        var time = Calls[i].TimeMS;
        var callCount = Calls[i].CallCount;
        string name = Owner.ppMap[Calls[i].ppid];

        buf.Append($"{i}({depth})");
        buf.Append(' ', depth + (depth < 9 ? 1 : 0));
        buf.AppendLine($"{name} Time: {time}/{Calls[i].ExclusiveTimeMS} Calls: {callCount}");
      }

      return buf.ToString();
    }

    public double TotalTimeMS => TotalTime * 1000000;

    public uint GameTime => Sections[(int)PSectionId.Game].Time;
    public double GameRatio => GameTime / TotalTimeMS;

    public uint EngineTime => Sections[(int)PSectionId.Engine].Time;
    public double EngineRatio => EngineTime / TotalTimeMS;

    public uint RenderingTime => Sections[(int)PSectionId.Rendering].Time;
    public double RenderingRatio => RenderingTime / TotalTimeMS;

    public ProfileThread MainThread { get; private set; }
  }
}
