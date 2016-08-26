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

namespace PerfAnalyzer {
  [Export(typeof(FrameTimeViewModel))]
  public class FrameTimeViewModel : Screen, IHandle<ProfileLog> {
    public LinearAxis LeftAxis { get; private set; }
    public LinearAxis BottomAxis { get; private set; }
    public LineSeries FrameTimeSeries { get; private set; }

    [Import]
    public IEventAggregator Events { get; set; }

    [ImportingConstructor]
    public FrameTimeViewModel(IEventAggregator events) {
      Events = events;

      DisplayName = "Frame Timeline";

      Model = new PlotModel();
      Model.LegendBackground = OxyColor.FromArgb(200, 255, 255, 255);
      Model.LegendBorder = OxyColors.Black;
      Model.LegendPlacement = LegendPlacement.Outside;
      Model.LegendPosition = LegendPosition.LeftTop;
      Model.Title = "Frame Timeline";

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
       // MinorStep = 1,
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
       // DataFieldX = nameof(FrameEntry.Time),
        //DataFieldY = nameof(FrameEntry.TimeTaken),
      };

      Model.Series.Add(FrameTimeSeries);

      SelectedTimeSeries = new LineSeries {
        Title = "Selected",
        // DataFieldX = nameof(FrameEntry.Time),
        //DataFieldY = nameof(FrameEntry.TimeTaken),
      };

      Model.Series.Add(SelectedTimeSeries);

      Ranges = PlotRangeTracker.Install(Model);

      Ranges.RangeCreated += Ranges_RangeCreated;
      Ranges.RangeMoved += Ranges_RangeCreated;

      PLog = new ProfileLog();
    }

    private void Ranges_RangeCreated(RectangleAnnotation range, double start, double end) {
      var frames = PLog.GetFramesInRange(start, end);

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

        if (frames.Count > 10000) {
          //frames = LargestTriangleThreeBuckets(frames, 10000);
        }

        var markedFrames = PLog.Frames.
                           Where(f => f.Markers.Any(m => m.Kind == MarkerKind.LUA_TRACESFLUSHED)).
                           Select(f => new {
                             Frame = f,
                             Marker = f.Markers.First(m => m.Kind == MarkerKind.LUA_TRACESFLUSHED)
                           }).
                           ToList();


        foreach (var item in markedFrames) {
          var annotation = new LineAnnotation() {
            X = item.Frame.EndTimeMS,
            Text = $"{item.Marker.Label} {item.Marker.UserValue}",
            ToolTip = "",
            Type = LineAnnotationType.Vertical,
          };
          Model.Annotations.Add(annotation);
        }


      } else {
        frames = new List<DataPoint>();
      }

      FrameTimeSeries.ItemsSource = frames;
      LeftAxis.Zoom(0, 30);
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

    private PerfNodeStats _selectedNode;

    public PerfNodeStats SelectedNode {
      get {
        return _selectedNode;
      }

      set {
        if (value != null) {
          var frameStats = PLog.GetNodeFrameStats(value.Id);
          long start = PLog.StartTime;
          SelectedTimeSeries.ItemsSource = frameStats.
            Select(f => new DataPoint(f.Frame.EndTimeMS, f.Time)).
            ToList();
        }
        _selectedNode = value;
        Model.InvalidatePlot(true);

        NotifyOfPropertyChange();
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
        NotifyOfPropertyChange();
      }
    }

    public PlotRangeTracker Ranges { get; private set; }
    public LineSeries SelectedTimeSeries { get; private set; }

    public static List<DataPoint> LargestTriangleThreeBuckets(List<DataPoint> data, int threshold) {
      int dataLength = data.Count;
      if (threshold >= dataLength || threshold == 0)
        return data; // Nothing to do

      List<DataPoint> sampled = new List<DataPoint>(threshold);

      // Bucket size. Leave room for start and end data points
      double every = (double)(dataLength - 2) / (threshold - 2);

      int a = 0;
      DataPoint maxAreaPoint = new DataPoint(0, 0);
      int nextA = 0;

      sampled.Add(data[a]); // Always add the first point

      for (int i = 0; i < threshold - 2; i++) {
        // Calculate point average for next bucket (containing c)
        double avgX = 0;
        double avgY = 0;
        int avgRangeStart = (int)(Math.Floor((i + 1) * every) + 1);
        int avgRangeEnd = (int)(Math.Floor((i + 2) * every) + 1);
        avgRangeEnd = avgRangeEnd < dataLength ? avgRangeEnd : dataLength;

        int avgRangeLength = avgRangeEnd - avgRangeStart;

        for (; avgRangeStart < avgRangeEnd; avgRangeStart++) {
          avgX += data[avgRangeStart].X; // * 1 enforces Number (value may be Date)
          avgY += data[avgRangeStart].Y;
        }
        avgX /= avgRangeLength;

        avgY /= avgRangeLength;

        // Get the range for this bucket
        int rangeOffs = (int)(Math.Floor((i + 0) * every) + 1);
        int rangeTo = (int)(Math.Floor((i + 1) * every) + 1);

        // Point a
        double pointAx = data[a].X; // enforce Number (value may be Date)
        double pointAy = data[a].Y;

        double maxArea = -1;

        for (; rangeOffs < rangeTo; rangeOffs++) {
          // Calculate triangle area over three buckets
          double area = Math.Abs((pointAx - avgX) * (data[rangeOffs].Y - pointAy) -
                                 (pointAx - data[rangeOffs].X) * (avgY - pointAy)
                            ) * 0.5;
          if (area > maxArea) {
            maxArea = area;
            maxAreaPoint = data[rangeOffs];
            nextA = rangeOffs; // Next a is this b
          }
        }

        sampled.Add(maxAreaPoint); // Pick this point from the bucket
        a = nextA; // This a is the next a (chosen b)
      }

      sampled.Add(data[dataLength - 1]); // Always add last

      return sampled;
    }
  }
}
