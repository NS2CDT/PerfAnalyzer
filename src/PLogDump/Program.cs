using PerformanceLog;
using System;
using System.IO;

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
  }
}
