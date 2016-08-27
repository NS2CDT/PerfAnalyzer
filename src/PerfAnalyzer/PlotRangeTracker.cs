using OxyPlot;
using OxyPlot.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OxyPlot.Series;

namespace PerfAnalyzer {
  public class PlotRangeTracker {
    public PlotModel Model { get; private set; }
    public List<RectangleAnnotation> Ranges { get; private set; }

    public int RangeLimit { get; set; } = 1;
    public bool CanCreateRanges { get; set; } = true;
    public bool CanChangeRanges { get; set; } = true;
    public event RangeEvent RangeMoved;
    public event RangeEvent RangeCreated;

    public List<RectangleAnnotation> SetRanges;

    private RectangleAnnotation _activeRange;
    private RectangleAnnotation _clickedRange;
    private double _clickStartX, _clickStartXOffset;
    private double _moveStartX;

    private PlotRangeTracker() {
      Ranges = new List<RectangleAnnotation>();
      SetRanges = new List<RectangleAnnotation>();
    }

    public static PlotRangeTracker Install(PlotModel model) {
      var result = new PlotRangeTracker();

      var series = model.Series.OfType<XYAxisSeries>().FirstOrDefault();

      if (series == null) {
        throw new Exception("a XYAxisSeries derived series needed tobe set on the plot");
      }

      result.PlotSeries = series;
      result.Model = model;
      model.MouseMove += result.Plot_MouseMove;
      model.MouseDown += result.Plot_MouseDown;
      model.MouseUp += result.Plot_MouseUp;

      return result;
    }

    private RectangleAnnotation CreateRange() {
      var range = new RectangleAnnotation {
        Fill = OxyColor.FromAColor(120, OxyColors.SkyBlue),
        MinimumX = 0,
        MaximumX = 0
      };
      Ranges.Add(range);
      range.MouseDown += Range_MouseDown;
      range.MouseUp += Range_MouseUp;
      Model.Annotations.Add(range);
      return range;
    }

    public void ReinstallAnnotations() {
      foreach (var range in Ranges) {
        Model.Annotations.Add(range);
      }
    }

    public void ClearRanges() {
      SetRanges.Clear();

      foreach (var range in Ranges) {
        range.MinimumX = 0;
        range.MaximumX = 0;
      }
    }

    private void RemoveRange(RectangleAnnotation range) {
      SetRanges.Remove(range);
      range.MinimumX = 0;
      range.MaximumX = 0;
      Model.InvalidatePlot(false);
    }

    private void Plot_MouseDown(object sender, OxyMouseDownEventArgs e) {

      if (e.ChangedButton == OxyMouseButton.Left) {
        MouseMoveState = MoveState.None;
      }

      if (!CanCreateRanges || e.ChangedButton != OxyMouseButton.Left || SetRanges.Count == RangeLimit) {
        return;
      }

      _clickStartX = PlotSeries.InverseTransform(e.Position).X;

      if (Ranges.Count <= SetRanges.Count) {
        _activeRange = CreateRange();
      } else {
        _activeRange = Ranges[SetRanges.Count];
      }

      SetRanges.Add(_activeRange);

      _activeRange.MinimumX = _clickStartX;
      _activeRange.MaximumX = _clickStartX;
      Model.InvalidatePlot(false);
      e.Handled = true;
      MouseMoveState = MoveState.CreatingRange;
    }

    private void Plot_MouseUp(object sender, OxyMouseEventArgs e) {

      if (MouseMoveState == MoveState.CreatingRange) {
        // Don't create the range if it does not have a size set
        if (_activeRange.MinimumX != _activeRange.MaximumX) {
          RangeCreated?.Invoke(_activeRange, _activeRange.MinimumX, _activeRange.MaximumX);
        } else {
          SetRanges.Remove(_activeRange);
        }
      }

      MouseMoveState = MoveState.None;
    }

    private void Plot_MouseMove(object sender, OxyMouseEventArgs e) {

      if (MouseMoveState == MoveState.None) {
        return;
      }

      var x = PlotSeries.InverseTransform(e.Position).X;

      if (MouseMoveState == MoveState.MovingRange) {
        var change = (x - _moveStartX) + _clickStartXOffset;
        var size = Math.Abs(_clickedRange.MaximumX - _clickedRange.MinimumX);

        _clickedRange.MinimumX = Math.Max(_moveStartX + change, 0);
        _clickedRange.MaximumX = _clickedRange.MinimumX + size;
        //model.Subtitle = string.Format("Integrating from {0:0.00} to {1:0.00}", range.MinimumX, range.MaximumX);
      } else {
        _activeRange.MinimumX = Math.Min(x, _clickStartX);
        _activeRange.MaximumX = Math.Max(x, _clickStartX);
      }

      _activeRange.Text = $"{_activeRange.MinimumX / 1000:F0} - {_activeRange.MaximumX / 1000:F0}";
      //model.Subtitle = string.Format("Integrating from {0:0.00} to {1:0.00}", range.MinimumX, range.MaximumX);
      Model.InvalidatePlot(false);
      e.Handled = true;
    }

    public enum MoveState {
      None,
      CreatingRange,
      MovingRange,
    }

    private MoveState _mouseMoveState;

    public MoveState MouseMoveState {
      get {
        return _mouseMoveState;
      }
      private set {
        if (value != MoveState.MovingRange) {
          _clickedRange = null;
        }
        _mouseMoveState = value;
      }
    }

    public XYAxisSeries PlotSeries { get; private set; }

    private void Range_MouseDown(object sender, OxyMouseDownEventArgs e) {

      _clickedRange = (RectangleAnnotation)sender;

      if (e.ChangedButton == OxyMouseButton.Left && e.IsControlDown) {
        RemoveRange(_clickedRange);
        e.Handled = true;
        return;
      }

      if (!CanChangeRanges || e.ChangedButton != OxyMouseButton.Left) {
        return;
      }

      var x = PlotSeries.InverseTransform(e.Position).X;

      _moveStartX = _clickedRange.MinimumX;
      _clickStartXOffset = -(x - _clickedRange.MinimumX);
      e.Handled = true;

      MouseMoveState = MoveState.MovingRange;
    }

    private void Range_MouseUp(object sender, OxyMouseEventArgs e) {
      var range = (RectangleAnnotation)_clickedRange;
      e.Handled = true;
      MouseMoveState = MoveState.None;

      //Don't send an event if were being removed
      if (range.MinimumX != 0 && range.MaximumX != 0) {
        RangeMoved?.Invoke(range, range.MinimumX, range.MaximumX);
      }
    }
  }

  public delegate void RangeEvent(RectangleAnnotation range, double start, double end);
}
