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


      var formatter = new TableFormatter();

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

      return;
    }
  }
}
