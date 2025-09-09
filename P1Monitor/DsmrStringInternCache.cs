using System.Text;

namespace P1Monitor;

/// <summary>
/// Simple cache for interning strings from byte arrays.
/// It is using a circular buffer, so it will not grow indefinitely. 
/// Fortunately, in the DSMR world we are not having too many individual strings we are interested in.
/// </summary>
public class DsmrStringInternCache(int size)
{
	private readonly CacheEntry[] _cache = new CacheEntry[size];
	private int _startIndex = 0; // Index of the oldest entry in the cache
	private int _endIndex = 0;   // before the cache is full, this is the index of the next free entry, otherwise it is always equal to _startIndex + _cache.Length 

    public string Get(ReadOnlySpan<byte> span)
	{
		int count = _endIndex - _startIndex;
		for (int i = 0; i < count; i++)
		{
			if (span.SequenceEqual(_cache[i].Bytes))
			{
				return _cache[i].Text;
			}
		}
		string result = Encoding.Latin1.GetString(span);
		if (count < _cache.Length)
		{
			_cache[_endIndex++] = new CacheEntry(span.ToArray(), result);
		}
		else
		{
			_cache[_startIndex++] = new CacheEntry(span.ToArray(), result);
			_endIndex++;
		}
		if (_startIndex == _cache.Length)
		{
			_startIndex -= _cache.Length;
			_endIndex -= _cache.Length;
		}
		return result;
	}

	private record struct CacheEntry(byte[] Bytes, string Text);
}