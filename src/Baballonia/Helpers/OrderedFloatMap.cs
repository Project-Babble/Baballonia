using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;

public class OrderedFloatMap : IReadOnlyDictionary<string, float>
{
    private readonly FrozenDictionary<string, int> _keyToIndex;
    private readonly float[] _values;

    public OrderedFloatMap(string[] orderedKeys)
    {
        _values = new float[orderedKeys.Length];

        // Create the temporary mapping to construct the FrozenDictionary
        var tempMap = new Dictionary<string, int>(orderedKeys.Length);
        for (int i = 0; i < orderedKeys.Length; i++)
        {
            tempMap[orderedKeys[i]] = i;
        }
        
        // FrozenDictionary heavily optimizes read-only lookups [3]
        _keyToIndex = tempMap.ToFrozenDictionary();
    }

    // Expose the raw array/span directly so the ONNX runner can write to it
    public Span<float> ValuesSpan => _values;

    // Getter/Setter indexer for application use
    public float this[string key]
    {
        get => _keyToIndex.TryGetValue(key, out int index) 
            ? _values[index] 
            : throw new KeyNotFoundException($"Key '{key}' not found.");
        set
        {
            if (_keyToIndex.TryGetValue(key, out int index))
                _values[index] = value;
            else
                throw new KeyNotFoundException($"Key '{key}' not found.");
        }
    }

    public bool TryGetValue(string key, out float value)
    {
        if (_keyToIndex.TryGetValue(key, out int index))
        {
            value = _values[index];
            return true;
        }
        value = 0f;
        return false;
    }

    // --- IReadOnlyDictionary Boilerplate ---
    public int Count => _values.Length;
    public bool ContainsKey(string key) => _keyToIndex.ContainsKey(key);
    public IEnumerable<string> Keys => _keyToIndex.Keys;
    public IEnumerable<float> Values => _values;

    public IEnumerator<KeyValuePair<string, float>> GetEnumerator()
    {
        foreach (var kvp in _keyToIndex)
        {
            yield return new KeyValuePair<string, float>(kvp.Key, _values[kvp.Value]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
