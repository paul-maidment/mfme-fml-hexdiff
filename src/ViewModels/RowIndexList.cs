using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace FmlDiff.ViewModels;

public sealed class RowIndexList : IList, IList<int>, INotifyCollectionChanged
{
    private int _count;

    public event NotifyCollectionChangedEventHandler CollectionChanged;

    public int Count => _count;

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    object IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public int this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return index;
        }
        set => throw new NotSupportedException();
    }

    public void SetCount(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        _count = count;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public bool Contains(int item) => item >= 0 && item < _count;

    public int IndexOf(int item) => item >= 0 && item < _count ? item : -1;

    public void CopyTo(int[] array, int arrayIndex)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        for (int i = 0; i < _count; i++)
            array[arrayIndex + i] = i;
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return i;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(int item) => throw new NotSupportedException();

    public void Clear() => SetCount(0);

    public void Insert(int index, int item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public bool Remove(int item) => throw new NotSupportedException();

    int IList.Add(object value) => throw new NotSupportedException();

    bool IList.Contains(object value) => value is int item && Contains(item);

    int IList.IndexOf(object value) => value is int item ? IndexOf(item) : -1;

    void IList.Insert(int index, object value) => throw new NotSupportedException();

    void IList.Remove(object value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        for (int i = 0; i < _count; i++)
            array.SetValue(i, index + i);
    }
}
