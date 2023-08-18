using System.Buffers;
using System.Text;

namespace P1Monitor;

public readonly struct TrimmedMemory : IDisposable, IEquatable<TrimmedMemory>
{
	private readonly byte[]? _ownedMemory;
	private readonly int _offset;
	private readonly int _length;
	
	public static readonly TrimmedMemory Empty = default;

	private TrimmedMemory(byte[]? ownedMemory, int offset, int length)
	{
		_ownedMemory = ownedMemory;
		_offset = offset;
		_length = length;
	}

	public Memory<byte> Memory => new Memory<byte>(_ownedMemory!).Slice(_offset, _length);

	public unsafe Span<byte> Span => new Span<byte>(_ownedMemory!).Slice(_offset, _length);

	public int Length => _length;

	public static TrimmedMemory Create(int length)
	{
		return new TrimmedMemory(ArrayPool<byte>.Shared.Rent(length), 0, length);
	}

	public static TrimmedMemory Create(ReadOnlySpan<byte> span)
	{
		var result = Create(span.Length);
		span.CopyTo(result.Span);
		return result;
	}

	public static TrimmedMemory Create(ReadOnlySpan<char> value)
	{
		var result = Create(value.Length);
		Encoding.Latin1.GetBytes(value, result.Span);
		return result;
	}

	public TrimmedMemory Slice(int offset)
	{
		return new TrimmedMemory(_ownedMemory!, _offset + offset, _length - offset);
	}

	public TrimmedMemory Slice(int offset, int length)
	{
		return new TrimmedMemory(_ownedMemory!, _offset + offset, length);
	}

	public void Dispose()
	{
		if (_ownedMemory != default)
		{
			ArrayPool<byte>.Shared.Return(_ownedMemory);
		}
	}

	public bool Equals(TrimmedMemory other)
	{
		if (_ownedMemory == default && other._ownedMemory == default) return true;
		if (_ownedMemory == default || other._ownedMemory == default) return false;
		return Span.SequenceEqual(other.Span);
	}

	public override int GetHashCode()
	{
		HashCode hashCode = new();
		hashCode.AddBytes(Span);
		return hashCode.ToHashCode();
	}

	public override string ToString()
	{
		return Encoding.Latin1.GetString(Span);
	}
}
