﻿using System.Collections;
using System.Text;

namespace VRCFaceTracking.Babble;

//  https://stackoverflow.com/questions/32761880/net-dictionary-with-two-keys-and-one-value

/// <summary>
/// A dictionary whose values can be accessed by two keys. Enforces unique outer keys and inner keys
/// </summary>
/// <remarks>
///  This isn't the fastest implementation, but it's best suited to adapt to varying OSC protocol naming schemes
/// </remarks>
/// <typeparam name="TKey1"></typeparam>
/// <typeparam name="TKey2"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class TwoKeyDictionary<TKey1, TKey2, TValue> : IEnumerable where TKey1 : notnull where TKey2 : notnull
{
    private readonly Dictionary<TKey1, TKey2> _mDic1 = new();
    private readonly Dictionary<TKey2, TValue> _mDic2 = new();
    private IEnumerator<TKey1>? _mCachedEnumerator;

    /// <summary>
    ///   Adds the specified key and value to the dictionary.
    ///   This will always add the value if the input parameters are not null.
    /// </summary>
    /// <param name="key1"></param>
    /// <param name="key2"></param>
    /// <param name="value"></param>
    /// <returns>Returns true if the value was added, false if the value was already present in the dictionary.</returns>
    public bool Add(TKey1 key1, TKey2 key2, TValue value)
    {
        if (key1 == null || key2 == null || ContainsKey1(key1) || ContainsKey2(key2))
        {
            return false;
        }

        _mDic1[key1] = key2;
        _mDic2[key2] = value;
        CacheEnumerator();

        return true;
    }

    /// <summary>
    /// Sets the specified key and value in the dictionary, if it already exists.
    /// This is functionally identical to the Add() method, but it will not add the value if it doesn't already exist.
    /// </summary>
    /// <param name="key1"></param>
    /// <param name="key2"></param>
    /// <param name="value"></param>
    /// <returns>Returns true if the value was set, false if the value was not found.</returns>
    public bool SetByAnyKey(TKey1 key1, TKey2 key2, TValue value)
    {
        if (!(ContainsKey1(key1) || ContainsKey2(key2)))
        {
            return false;
        }

        _mDic1[key1] = key2;
        _mDic2[key2] = value;
        CacheEnumerator();
        return true;
    }

    /// <summary>
    /// Sets the specified value in the dictionary given the first key, if it already exists.
    /// </summary>
    /// <param name="key1"></param>
    /// <param name="value"></param>
    /// <returns>Returns true if the value was set, false if the value was not found.</returns>
    public bool SetByKey1(TKey1 key1, TValue value)
    {
        if (!ContainsKey1(key1))
        {
            return false;
        }

        _mDic2[_mDic1[key1]] = value;
        CacheEnumerator();
        return true;
    }

    /// <summary>
    /// Sets the specified value in the dictionary given the second key, if it already exists.
    /// </summary>
    /// <param name="key2"></param>
    /// <param name="value"></param>
    /// <returns>Returns true if the value was set, false if the value was not found.</returns>
    public bool SetByKey2(TKey2 key2, TValue value)
    {
        if (!ContainsKey2(key2))
        {
            return false;
        }

        _mDic2[key2] = value;
        return true;
    }


    /// <summary>
    /// Gets the value associated with the outer key.
    /// </summary>
    /// <param name="key1"></param>
    /// <returns>The TValue for this Tkey1. </returns>
    public TValue GetByKey1(TKey1 key1)
    {
        return _mDic2[_mDic1[key1]];
    }

    /// <summary>
    /// Gets the value associated with the inner key.
    /// </summary>
    /// <param name="key2"></param>
    /// <returns>The TValue for this Tkey2. </returns>
    public TValue GetByKey2(TKey2 key2)
    {
        return _mDic2[key2];
    }

    /// <summary>
    /// Gets the value associated with the outer key.
    /// </summary>
    /// <param name="key1"></param>
    /// <param name="value"></param>
    /// <returns>The TValue for this Tkey1. </returns>
    public bool TryGetByKey1(TKey1 key1, out TValue value)
    {
        if (!ContainsKey1(key1))
        {
            value = default!;
            return false;
        }

        if (!ContainsKey2(_mDic1[key1]))
        {
            value = default!;
            return false;
        }

        value = _mDic2[_mDic1[key1]];
        return true;
    }

    /// <summary>
    /// Gets the value associated with the inner key.
    /// </summary>
    /// <param name="key2"></param>
    /// <param name="value"></param>
    /// <returns>The TValue for this Tkey2. </returns>
    public bool TryGetByKey2(TKey2 key2, out TValue value)
    {
        if (!ContainsKey2(key2))
        {
            value = default!;
            return false;
        }

        value = _mDic2[key2];
        return true;
    }

    /// <summary>
    /// Removes the value associated with the outer key. If the outer key is not found, nothing happens. If the outer key is found, the inner key must also exist and is also removed.
    /// </summary>
    /// <param name="key1"></param>
    public bool RemoveByKey1(TKey1 key1)
    {
        if (!_mDic1.Remove(key1, out TKey2 tmpKey2))
        {
            return false;
        }

        _mDic2.Remove(tmpKey2);
        CacheEnumerator();
        return true;
    }

    /// <summary>
    /// Removes the value associated with the inner key. If the inner key is not found, nothing happens. If the inner key is found, the outer key must also exist and is also removed.
    /// </summary>
    /// <param name="key2"></param>
    public bool RemoveByKey2(TKey2 key2)
    {
        if (!_mDic2.ContainsKey(key2))
        {
            return false;
        }

        TKey1 tmpKey1 = _mDic1.First((kvp) => kvp.Value.Equals(key2)).Key;
        _mDic1.Remove(tmpKey1);
        _mDic2.Remove(key2);
        CacheEnumerator();
        return true;
    }

    /// <summary>
    /// Get the length of this two-key dictionary. m_dic1.Count is used, as it is the same as m_dic2.Count.
    /// </summary>
    public int Count
    {
        get
        {
            return _mDic1.Count;
        }
    }

    /// <summary>
    /// Evaluates whether the two-key dictionary contains the outer key.
    /// </summary>
    /// <param name="key1"></param>
    /// <returns>True if the outer dictionary contains the key.</returns>
    public bool ContainsKey1(TKey1 key1)
    {
        return _mDic1.ContainsKey(key1);
    }

    /// <summary>
    /// Evaluates whether the two-key dictionary contains the inner key.
    /// </summary>
    /// <param name="key2"></param>
    /// <returns>True if the inner dictionary contains the key.</returns>
    public bool ContainsKey2(TKey2 key2)
    {
        return _mDic2.ContainsKey(key2);
    }

    /// <summary>
    /// Evaluates whether the two-key dictionary contains the given value.
    /// </summary>
    /// <param name="value"></param>
    /// <returns>True if the two-key dictionary contains the value.</returns>
    public bool ContainsValue(TValue value)
    {
        return _mDic2.ContainsValue(value);
    }

    public (TKey1, TKey2, TValue) ElementAt(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new IndexOutOfRangeException();
        }
        return (_mDic1.ElementAt(index).Key, _mDic1.ElementAt(index).Value, _mDic2[_mDic1.ElementAt(index).Value]);
    }

    /// <summary>
    /// Clears the two-key dictionary.
    /// </summary>
    public void Clear()
    {
        _mDic1.Clear();
        _mDic2.Clear();
        CacheEnumerator();
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<TKey1, TKey2> kvp in _mDic1)
        {
            sb.AppendLine($"Key1: {kvp.Key}, Key2: {kvp.Value}, Value: {_mDic2[kvp.Value]}");
        }
        return sb.ToString();
    }

    public IEnumerable<TKey1> OuterKeys => _mDic1.Keys;

    public IEnumerable<TKey2> InnerKeys => _mDic2.Keys;

    private void CacheEnumerator()
    {
        _mCachedEnumerator = _mDic1.Keys.GetEnumerator();
    }

    public IEnumerator<TKey1>? GetEnumerator()
    {
        _mCachedEnumerator?.Reset();
        return _mCachedEnumerator;
    }

    IEnumerator? IEnumerable.GetEnumerator() => GetEnumerator();
}
