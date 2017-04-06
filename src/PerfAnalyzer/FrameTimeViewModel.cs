using Caliburn.Micro;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using PerformanceLog;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Reactive;
using System.Reactive.Linq;
using MoreLinq;

namespace PerfAnalyzer {
  [Export(typeof(FrameTimeViewModel))]
  public class FrameTimeViewModel : Screen, IHandle<ProfileLog> {
    public LinearAxis LeftAxis { get; private set; }
    public LinearAxis BottomAxis { get; private set; }
    public LineSeries FrameTimeSeries { get; private set; }
    public LineSeries SelectedTimeSeries { get; private set; }
    public PlotRangeTracker Ranges { get; private set; }
    public PlotDownsampler DownSampler { get; private set; }

    [Import]
    public IEventAggregator Events { get; set; }

    [ImportingConstructor]
    public FrameTimeViewModel(IEventAggregator events) {
      Events = events;

      DisplayName = "Frame Timeline";

      Model = new PlotModel();
      Model.LegendBackground = OxyColor.FromArgb(200, 255, 255, 255);
      Model.LegendBorder = OxyColors.Black;
      Model.LegendPlacement = LegendPlacement.Inside;
      Model.LegendPosition = LegendPosition.RightTop;
      Model.Title = "Frame Timeline";

      DownSampler = new PlotDownsampler();

      /*
            var timeLineAxis = new TimeSpanAxis() {
              MajorGridlineStyle = LineStyle.Solid,
              MinorGridlineStyle = LineStyle.Dot,
              IntervalLength = 30,
              MajorStep = 1,
              Position = AxisPosition.Bottom,
              AbsoluteMinimum = 0,
            };
      */
      BottomAxis = new LinearAxis() {
        AbsoluteMinimum = 0,
        Position = AxisPosition.Bottom,
        MinorStep = 1000,
        MajorStep = 1000*60,
      };

      BottomAxis.AxisChanged += TimeLineAxis_AxisChanged;
      BottomAxis.LabelFormatter = t => $"{t/60000}";

      Model.Axes.Add(BottomAxis);
      /*
      var dateTimeAxis = new DateTimeAxis {
        Position = AxisPosition.Bottom,
        IntervalType = DateTimeIntervalType.Seconds,
        MinorIntervalType = DateTimeIntervalType.Milliseconds,
        //IntervalLength = 50
      };

      plotModel.Axes.Add(dateTimeAxis);
      */

      LeftAxis = new LinearAxis() {
        AbsoluteMinimum = 0,
        Position = AxisPosition.Left,
        MinorStep = 1,
        MajorStep = 10,
        Title = "Time Ms",
      };
      Model.Axes.Add(LeftAxis);
      LeftAxis.Zoom(0, 40);

      FrameTimeSeries = new LineSeries {
        Title = "Frame",
        StrokeThickness = 1,
       // DataFieldX = nameof(FrameEntry.Time),
        //DataFieldY = nameof(FrameEntry.TimeTaken),
      };

      Model.Series.Add(FrameTimeSeries);
      DownSampler.AddSeries(FrameTimeSeries, this.OnPropertyChanges(n => FrametimePoints));

      SelectedTimeSeries = new LineSeries {
        Title = "Selected",
        StrokeThickness = 1,
        Color = OxyColors.Red,
      };

      Model.Series.Add(SelectedTimeSeries);
      DownSampler.AddSeries(SelectedTimeSeries, this.OnPropertyChanges(n => SelectedNode).
                                                Where(n => n != null || PLog.NodeStats.Count == 0).
                                                Select(n => GetNodeDataPoints(n)));

      UpdateWorldJobTimeSeries = new LineSeries {
        Title = "UpdateWorldJob",
        StrokeThickness = 1,
        Color = OxyColors.Black,
      };

      Model.Series.Add(UpdateWorldJobTimeSeries);
      DownSampler.AddSeries(UpdateWorldJobTimeSeries, this.OnPropertyChanges(n => FrametimePoints).
                            Where(n => this.PLog.GetNameId("UpdateWorldJob::Run") != -1).
                            Select(n => GetNodeDataPoints(this.PLog.GetNodeStats("UpdateWorldJob::Run"), f => f.InclusiveTime)));

      Ranges = PlotRangeTracker.Install(Model);

      Ranges.RangeCreated += Ranges_RangeCreated;
      Ranges.RangeMoved += Ranges_RangeCreated;
      Ranges.RangeRemoved += (r, min, max) => { NodeList = PLog.NodeStats; };

      PLog = new ProfileLog();
    }

    private void Ranges_RangeCreated(RectangleAnnotation range, double start, double end) {
      var frames = PLog.GetFramesInRange(start, end);

      if (frames.Count == 0) {
        NodeList = new List<PerfNodeStats>();
        range.ToolTip = "No Frames!";
        return;
      }

      range.ToolTip = $"{frames.Count} frames, Span {(end-start)/1000:F3}s, Avg: {frames.Average(f => f.Time):F2}Ms  Peak {frames.MaxBy(f=> f.Time).Time:F2}Ms";

      NodeList = PLog.GetStatsForRange(frames.Start, frames.End).
                 OrderByDescending(p => p.AvgExclusiveTime).
                 ToList();
    }

    private void TimeLineAxis_AxisChanged(object sender, AxisChangedEventArgs e) {

      if (e.ChangeType == AxisChangeTypes.Zoom) {

      }
    }

    //handle new ProfileLog opened message
    public void Handle(ProfileLog newProfileLog) {
      PLog = newProfileLog;

      Ranges.ClearRanges();
      Model.Annotations.Clear();
      Ranges.ReinstallAnnotations();

      List<DataPoint> frames;

      if (PLog.Frames.Count != 0) {

        long start = PLog.StartTime;
        frames = PLog.Frames.Select(f => new DataPoint(f.EndTimeMS, f.Time)).ToList();

        var markedFrames = PLog.Frames.
                           Where(f => f.Markers.Length != 0).
                           Select(f => f.Markers.FirstOrDefault(m => m.Kind == MarkerKind.LUA_TRACESFLUSHED) ?? f.Markers.First()).
                           ToList();

        var flushFrames = new HashSet<ProfileFrame>();

        foreach (var marker in markedFrames) {
          flushFrames.Add(marker.Frame);

          var text = $"{marker.Kind}";

          if (marker.Kind == MarkerKind.LUA_TRACESFLUSHED) {
            text = $"Trace Flush {marker.Label} Thread: {marker.Frame.Threads[marker.ThreadId].Name}";
          } else if (marker.Kind == MarkerKind.FOCUS_LOST || marker.Kind == MarkerKind.FOCUS_GAINED) {
            text = $"{marker.Label}";
          } else if(marker.UserValue != 0) {
            text = $"{marker.Label} {marker.UserValue} Thread: {marker.Frame.Threads[marker.ThreadId].Name}";
          }

          var annotation = new LineAnnotation() {
            X = marker.Frame.EndTimeMS,
            Text = text,
            Tag = marker,
            ToolTip = "",
            Type = LineAnnotationType.Vertical,
          };
          Model.Annotations.Add(annotation);
        }

        var lj = PLog.GetMatchingNodes("lj");

        if (lj.Count != 0) {
          foreach (var item in PLog.GetNodeFrameStats(lj.First().Id)) {

            if (flushFrames.Contains(item.Frame)) {
              continue;
            }

            var annotation = new LineAnnotation() {
              X = item.Frame.EndTimeMS,
              Text = $"Trace Flush",
              Tag = item,
              Type = LineAnnotationType.Vertical,
            };
            Model.Annotations.Add(annotation);
          }
        }

      } else {
        frames = new List<DataPoint>();
      }

      FrametimePoints = frames;

      LeftAxis.Zoom(0, 60);
      BottomAxis.ZoomAt(1, 0);
      Model.InvalidatePlot(true);
    }

    public PlotModel Model { get; set; }

    private ProfileLog _profileLog;

    public ProfileLog PLog {
      get {
        return _profileLog;
      }

      set {
        _profileLog = value;

        if (value.Frames.Count == 0) {
          Ranges.CanCreateRanges = false;
        } else {
          Ranges.CanCreateRanges = true;
        }

        NotifyOfPropertyChange();
        NodeList = PLog.NodeStats;
      }
    }

    public void ShowFrame() {

      if (PLog.Frames.Count ==  0)
      {
        return;
      }

      var frame = PLog.Frames[0];

      if (Ranges.Ranges.Count != 0) {
        var range = Ranges.Ranges.Last();
        var frames = PLog.GetFramesInRange(range.MinimumX, range.MaximumX);

        frame = frames.OrderByDescending(f => f.MainThread.TimeMs).First();
      }

      Events.PublishOnUIThread(new ShowFrameDetails(frame));
    }

    public void ClearSelection() {
      Ranges.ClearRanges();
    }

    private List<DataPoint> _rawFrametimePoints;

    public List<DataPoint> FrametimePoints {
      get {
        return _rawFrametimePoints;
      }

      set {
        _rawFrametimePoints = value;
        NotifyOfPropertyChange();
      }
    }

    public bool DownsampleGraph {
      get {
        return DownSampler.Downsample;
      }

      set {
        DownSampler.Downsample = value;
        NotifyOfPropertyChange();
      }
    }

    public int DownsampleLimit {
      get {
        return DownSampler.DownsampleLimit;
      }

      set {
        DownSampler.DownsampleLimit = value;
        NotifyOfPropertyChange();
      }
    }

    private List<DataPoint> GetNodeDataPoints(PerfNodeStats node) {
      if (node != null) {
        var frameStats = PLog.GetNodeFrameStats(node.Id);
        return frameStats.
               Select(f => new DataPoint(f.Frame.EndTimeMS, f.Time)).
               ToList();
      } else {
        return new List<DataPoint>();
      }
    }

    private List<DataPoint> GetNodeDataPoints(PerfNodeStats node, Func<ProfileLog.NodeFrameEntry, double> fieldpick) {
      if (node != null) {
        var frameStats = PLog.GetNodeFrameStats(node.Id);
        var points = new List<DataPoint>();

        for (int i = 0; i < frameStats.Count; i++) {
          points.Add(new DataPoint(frameStats[i].Frame.EndTimeMS, fieldpick(frameStats[i])));
        }

        return points;
      } else {
        return new List<DataPoint>();
      }
    }

    private PerfNodeStats _selectedNode;

    public PerfNodeStats SelectedNode {
      get {
        return _selectedNode;
      }

      set {
        _selectedNode = value;
        NotifyOfPropertyChange();
      }
    }


    private string _nodeFilter;

    public string NodeFilter {
      get {
        return _nodeFilter;
      }

      set {
        _nodeFilter = value;
        UpdateNodeFilter();
        NotifyOfPropertyChange();
      }
    }

    private void UpdateNodeFilter() {
      if (string.IsNullOrWhiteSpace(NodeFilter)) {
        CollectionViewSource.GetDefaultView(NodeList).Filter = null;
      } else {
        CollectionViewSource.GetDefaultView(NodeList).Filter = (object n) => {
          return ((PerfNodeStats)n).Name.IndexOf(NodeFilter, StringComparison.InvariantCultureIgnoreCase) != -1;
        };
      }
    }

    private List<PerfNodeStats> _nodeList;

    public List<PerfNodeStats> NodeList {
      get {
        return _nodeList;
      }

      set {
        var sorts = _nodeList != null ? CollectionViewSource.GetDefaultView(_nodeList).SortDescriptions.ToList() : null;
        _nodeList = value;

        if (sorts != null) {
          foreach (var sort in sorts) {
            CollectionViewSource.GetDefaultView(value).SortDescriptions.Add(sort);
          }
        }
        CollectionViewSource.GetDefaultView(value).Refresh();
        UpdateNodeFilter();
        NotifyOfPropertyChange();
      }
    }

    public LineSeries UpdateWorldJobTimeSeries { get; private set; }
  }
}
