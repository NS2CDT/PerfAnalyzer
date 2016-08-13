using System;
using System.Diagnostics;

namespace PerformanceLog {

  public struct Timer : IDisposable {

    internal static Stopwatch _timer = MakeStopwatch();
    private readonly long _start;
    private readonly string _description;

    static Stopwatch MakeStopwatch() {
      var res = new Stopwatch();
      res.Start();
      return res;
    }

    public Timer(string description, bool subTimer = false) {
      if (!subTimer) {
        GC.Collect();
      }

      _start = _timer.ElapsedMilliseconds;
      _description = description;
    }

    public void Dispose() {
      var end = _timer.ElapsedMilliseconds;
      Console.WriteLine($"{_description}: {end - _start}ms elapsed");
    }
  }
}
