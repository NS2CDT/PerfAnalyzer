using LinqStatistics;
using PerformanceLog;
using System;
using System.IO;
using System.Linq;
using Tababular;

namespace PLogDump {

  class Program {
    static void Main(string[] args) {

      if (args.Length == 0) {
        Console.Error.WriteLine("Expected plog path");
        return;
      }

      var path = args[0];

      if (!File.Exists(path)) {
        Console.Error.WriteLine($"plog file '{path}' doesn't exist");
        return;
      }

      ProfileLog log;

      using (new Timer("Loading plog")) {
        log = new ProfileLog(path);
      }


      return;
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
