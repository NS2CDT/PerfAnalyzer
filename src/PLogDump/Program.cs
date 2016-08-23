using LinqStatistics;
using PerformanceLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tababular;
using MoreLinq;

namespace PLogDump {

  class Program {
    static void Main(string[] args) {
      string path = "";

      if (args.Length == 0) {
        string appdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Natural Selection 2");
        var plogs = new DirectoryInfo(appdata).EnumerateFiles("*.plog").ToList();
        var newestPlog = plogs.MaxBy(f => f.CreationTime);

        if (plogs.Count == 0) {
          Console.Error.WriteLine("Expected plog path");
          return;
        } else {
          Console.WriteLine("No plog path provided defaulting to loading last created plog in %appdata%/Natural Selection 2");
        }

        path = Path.Combine(appdata, newestPlog.Name);

      } else {
        path = args[0];
      }

      if (!File.Exists(path)) {
        Console.Error.WriteLine($"plog file '{path}' doesn't exist");
        return;
      }

      ProfileLog log1, log2;

      using (new Timer("Loading plog")) {
        log1 = new ProfileLog(path);
      }

      PrintStats(log1);

      var path2 = args.Length > 1 ? args[1] : null;

      if (path2 != null) {
        using (new Timer("Loading plog")) {
          log2 = new ProfileLog(path2);
        }

        var diff = log2.GetNodeStatsDiff(log1.NodeStats);
        diff = diff.OrderByDescending(n => n.Old.AvgExclusiveTime).ToList();
        var diff2 = diff.OrderByDescending(n => n.New.AvgExclusiveTime).ToList();

        var ModelMixin = GetNameDiff(log1, log2, "ModelMixin");


      }

      return;
    }

    public static List<NodeStatsDiff> GetNameDiff(ProfileLog old, ProfileLog _new, string name) {
      var nodes = old.GetMatchingNodes(name);
      return _new.GetNodeStatsDiff(nodes).OrderByDescending(n => n.Old.AvgExclusiveTime).ToList();
    }

    static void PrintStats(ProfileLog log) {
      var formatter = new TableFormatter();
      Console.WriteLine();
      Console.WriteLine();

      var frames = log.Frames.Skip(2).Where(f => f.Calls.Length != 0);
      var frameTimes = frames.Where(f => f.RawTime > 0).Select(f => f.RawTime).ToArray();
      Array.Sort(frameTimes);

      var avgFrameMs = frameTimes.Average() / 1000.0;
      var medianFrameMs = frameTimes.Median() / 1000.0;

      var percentile90 = frameTimes.Take((int)(frameTimes.Length * 0.90)).Average() / 1000.0;

      var toppercentile90 = frameTimes.Skip((int)(frameTimes.Length * 0.90)).Average() / 1000.0;
      var top1percent = frameTimes.Skip((int)(frameTimes.Length * 0.99)).Average() / 1000.0;

      Console.WriteLine($"FrameTime Avg: {avgFrameMs:F3} Median: {medianFrameMs:F3} top 10%: {toppercentile90:F3}\n");

      var gcframes = frames.Select(f => (f.MainThread?.GCTime ?? 0)* 10.0 / 1000.0).ToList();
      var maxFrameStep = gcframes.OrderByDescending(t => t).ToList();

      if (log.WaitForGCJobId != -1) {
        PrintNode(log.NodeLookup[log.WaitForGCJobId], "WaitForGC");

        var gcTimesFull = log.GetNodeTimes(log.WaitForGCJobId);
        Array.Sort(gcTimesFull);

        var gcTimes = gcTimesFull.SkipWhile(t => t == 0).ToArray();

        var gcMedian = gcTimes.Median() * 10 / 1000.0;

        var gcVariance = gcTimes.Variance() * 10 / 1000.0;

        var gc90avg = gcTimes.Skip((int)(gcTimes.Length * 0.90)).Average() * 10 / 1000.0;

        var gc99avg = gcTimes.Skip((int)(gcTimes.Length * 0.99)).Average() * 10 / 1000.0;
      }

      if (log.WaitForGPUId != -1) {
        PrintNode(log.NodeLookup[log.WaitForGPUId], "WaitForGPU");
      }


      var top15 = log.NodeStats.
                  OrderByDescending(n => n.AvgExclusiveTime).
                  Take(15).
                  Select(n => new {
                    Name = n.Name,
                    AvgTime = n.AvgExclusiveTime,
                    PeakAvg = n.MaxAvgExclusiveTime,
                    TotalTime = n.TotalExclusiveTime,
                    Calls = n.CallCount,
                  });

      Console.Write(formatter.FormatObjects(top15));
    }

    static void PrintNode(PerfNodeStats node, string label = null) {
      Console.WriteLine($"{label ?? node.Name:F4}: avg = {node.AvgExclusiveTime:F4} PeakFrame = {node.PeakFrameTime:F4}");

    }
  }
}
