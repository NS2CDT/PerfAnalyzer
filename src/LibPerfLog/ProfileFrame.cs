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

  public class ProfileFrame {
    public long StartTime { get; internal set; }
    public long EndTime { get; private set; }
    public long RawTime { get; internal set; }
    public double Time { get; internal set; }
    public double TotalTime;

    public ProfileSection[] Sections;
    public CallRecord[] Calls;
    public ProfileMarker[] Markers;

    public ProfileThread MainThread { get; private set; }
    public List<ProfileThread> Threads { get; private set; }
    public uint GCCount { get; private set; }
    public int MaxDepth { get; internal set; }

    public uint FrameIndex { get; private set; }
    public ProfileLog Owner { get; private set; }
    public ProfileFrame PrevFrame => FrameIndex > 0 ? Owner.Frames[(int)FrameIndex - 1] : null;
    public ProfileFrame NextFrame => FrameIndex < Owner.Frames.Count ? Owner.Frames[(int)FrameIndex +1] : null;

    public double TotalTimeMS => TotalTime * 1000000;
    public double StartTimeMS => StartTime / 1000.0;
    public double EndTimeMS => EndTime / 1000.0;

    public uint GameTime => Sections[(int)PSectionId.Game].Time;
    public double GameRatio => GameTime / TotalTimeMS;

    public uint EngineTime => Sections[(int)PSectionId.Engine].Time;
    public double EngineRatio => EngineTime / TotalTimeMS;

    public uint RenderingTime => Sections[(int)PSectionId.Rendering].Time;
    public double RenderingRatio => RenderingTime / TotalTimeMS;

    public ProfileFrame(ProfileLog owner, uint frameIndex, long startTime, ProfileSection[] sections) {
      Owner = owner;
      FrameIndex = frameIndex;
      EndTime = startTime;
      Sections = sections;
      Calls = Array.Empty<CallRecord>();
      Markers = Array.Empty<ProfileMarker>();
    }

    internal void SetCalls(CallRecord[] calls, int callCount) {
      var compacted = new CallRecord[callCount];
      Calls = compacted;

      Array.Copy(calls, compacted, callCount);
    }

    internal void SetStartTime(long start) {
      Debug.Assert(start < EndTime);
      StartTime = start;
      RawTime = EndTime - start;
      // Scale millisecond to micro
      Time = RawTime / 1000.0;
    }

    public void ComputeTime() {
      Threads = new List<ProfileThread>();

      if (Calls == null) {
        Calls = Array.Empty<CallRecord>();
        return;
      }

      int threadNodeId = Owner.Threadppid;
      int gcNodeId = Owner.ScriptGC_STEP;

      int prevDepth = 0, parent = 0;
      ProfileThread thread = null;

      var depthToParent = new int[MaxDepth + 1];

      for (int i = 1; i < Calls.Length; i++) {
        int depth = Calls[i].Depth;
        var time = Calls[i].Time;

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
          if (thread != null) {
            thread.AddIdleTime(time, Calls[i].CallCount);
          }
          SubTime(i, depthToParent);
        }

        if (Calls[i].ppid == gcNodeId && thread.Name != "CollectGarbageJob::Run") {
          if (thread != null) {
            // Lift out the GC nodes so they don't distort actual CPU costs when analysing
            thread.AddGCTime(time, Calls[i].CallCount);
          }

          // Subtract time from all the parents of this Node
          SubTime(i, depthToParent);
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
        Calls[j].Time = (uint)Math.Max(parentTime, 0);

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

      if (ownerId == Owner.ServerGameUpdateId || ownerId == Owner.ClientGameUpdateId) {
        thread.Flags |= ProfileThreadFlag.MainThread;
        MainThread = thread;
      }

      Threads.Add(thread);
      return thread;
    }

    public bool NodeHasChildren(int nodeIndex) {
      int minDepth = Calls[nodeIndex].Depth;

      return nodeIndex < Calls.Length && Calls[nodeIndex+1].Depth > minDepth;
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

    public IEnumerable<int> GetChildNodesIndexs(int nodeIndex) {
      int minDepth = Calls[nodeIndex].Depth;

      for (int i = nodeIndex + 1; i < Calls.Length; i++) {

        if (Calls[i].Depth <= minDepth) {
          yield break;
        }

        if (Calls[i].Depth == minDepth + 1) {
          yield return i;
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

    public PerfNodeStats GetPeakNode() {
      int peakIndex = -1;
      uint peak = 0;

      for (int i = 1; i < Calls.Length; i++) {

        if (Calls[i].ExclusiveTime > peak) {
          peak = Calls[i].ExclusiveTime;
          peakIndex = i;
        }
      }

      var node = new PerfNodeStats(Owner, Owner.ppMap[Calls[peakIndex].ppid], Calls[peakIndex].ppid);
      node.SetStats(Calls[peakIndex].ExclusiveTime, Calls[peakIndex].ExclusiveTime/ Calls[peakIndex].CallCount, Calls[peakIndex].CallCount, 1);
      return node;
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

    public override string ToString() {
      return $"Time: {Time:F3}ms Id:{FrameIndex}";
    }
  }
}
