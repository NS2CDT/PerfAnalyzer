using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceLog {

  public static class ExternsionMethords {

    public static int BinarySearch<T>(this IList<T> list, uint bound, Func<T, uint> getkey, int listsize = -1)  {
      var low = 0;
      var high = listsize == -1 ? list.Count : listsize;
      high--;

      while (low <= high) {
        var middle = low + ((high - low) >> 1);
        var midValue = list[middle];

        int comparison = (int)bound - (int)getkey(midValue);
        if (comparison == 0) {
          return middle;
        }

        if (comparison < 0) {
          high = middle - 1;
        } else {
          low = middle + 1;
        }
      }

      return -low;
    }

    public static int BinarySearch<T>(this IList<T> list, int bound, Func<T, int> getkey, int listsize = -1) {
      var low = 0;
      var high = listsize == -1 ? list.Count : listsize;
      high--;

      while (low <= high) {
        var middle = low + ((high - low) >> 1);
        var midValue = list[middle];

        int comparison = bound - getkey(midValue);
        if (comparison == 0) {
          return middle;
        }

        if (comparison < 0) {
          high = middle - 1;
        } else {
          low = middle + 1;
        }
      }

      return -low;
    }

    public static int BinarySearch<T>(this IList<T> list, double bound, Func<T, double> getkey, double epsilon = 0.0001, int listsize = -1) {
      var low = 0;
      var high = listsize == -1 ? list.Count : listsize;
      high--;

      while (low <= high) {
        var middle = low + ((high - low) >> 1);
        var midValue = list[middle];

        var comparison = bound - getkey(midValue);
        if (Math.Abs(comparison) < epsilon) {
          return middle;
        }

        if (comparison < 0) {
          high = middle - 1;
        } else {
          low = middle + 1;
        }
      }

      return -low;
    }

    public static ListSlice<T> GetSlice<T>(this IList<T> list, int startId, int endId, Func<T, int> keyGetter)  {

      int start = list.BinarySearch(startId, keyGetter);
      int end = list.BinarySearch(endId, keyGetter);

      if (start < 0) {
        start = -start;
      }

      if (end < 0) {
        end = -end;
      }

      Debug.Assert((end - start) >= 0);
      return new ListSlice<T>(list, start, end - start);
    }
  }
}
