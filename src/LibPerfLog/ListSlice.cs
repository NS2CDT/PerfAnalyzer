using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerformanceLog {

  public class ListSlice<T> : IList<T> {
    private IList<T> _list;
    public int Start { get; }
    public int End => Start + Count;
    public int Count { get; private set; }

    public ListSlice(IList<T> list, int start, int count) {
      _list = list;
      Start = start;
      Count = count;
    }

    public ListSlice(IList<T> list) {
      _list = list;
      Start = 0;
      Count = list.Count;
    }

    public ListSlice(IList<T> list, int start) {
      _list = list;
      Start = start;
      Count = list.Count - start;
    }

    public T this[int index] {
      get { return _list[Start + index]; }
      set { throw new NotImplementedException(); }
    }

    public IEnumerator<T> GetEnumerator() {
      int end = Start + Count;
      for (int i = Start; i < end; i++) {
        yield return _list[i];
      }
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }

    public bool IsReadOnly => true;

    public void Add(T item) {
      throw new NotImplementedException();
    }

    public void Clear() {
      throw new NotImplementedException();
    }

    public bool Contains(T item) {
      return IndexOf(item) != -1;
    }

    public void CopyTo(T[] array, int arrayIndex) {
      for (int i = Start; i < End; i++) {
        array[arrayIndex + (i - Start)] = _list[i];
      }
    }

    public int IndexOf(T item) {
      if (_list is Array) {
        return Array.IndexOf((Array)_list, item, Start, Count);
      } else if (_list is List<T>) {
        return ((List<T>)_list).IndexOf(item, Start, Count);
      } else {
        throw new NotImplementedException();
      }
    }

    public void Insert(int index, T item) {
      throw new NotImplementedException();
    }

    public bool Remove(T item) {
      throw new NotImplementedException();
    }

    public void RemoveAt(int index) {
      throw new NotImplementedException();
    }
  }

}
