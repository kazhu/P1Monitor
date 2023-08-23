using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace P1Monitor;

public readonly struct TrimmedMemory : IDisposable, IEquatable<TrimmedMemory>
{
	private readonly byte[] _ownedMemory;
	private readonly int _offset;
	private readonly int _length;

	public TrimmedMemory(ReadOnlySpan<byte> span)
	{
		_ownedMemory = ArrayPool<byte>.Shared.Rent(span.Length);
		_offset = 0;
		_length = span.Length;
		span.CopyTo(_ownedMemory);
	}

	public Memory<byte> Memory => new Memory<byte>(_ownedMemory).Slice(_offset, _length);

	public readonly void Dispose()
	{
		if (_ownedMemory != default)
		{
			ArrayPool<byte>.Shared.Return(_ownedMemory);
		}
	}

	public static bool operator ==(TrimmedMemory left, TrimmedMemory right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(TrimmedMemory left, TrimmedMemory right)
	{
		return !(left == right);
	}

	public override bool Equals([NotNullWhen(true)] object? obj)
	{
		return obj is TrimmedMemory memory && Equals(memory);
	}

	public bool Equals(TrimmedMemory other)
	{
		if (_ownedMemory == default && other._ownedMemory == default) return true;
		if (_ownedMemory == default || other._ownedMemory == default) return false;
		return Memory.Span.SequenceEqual(other.Memory.Span);
	}

	public override int GetHashCode()
	{
		HashCode hashCode = new();
		hashCode.AddBytes(Memory.Span);
		return hashCode.ToHashCode();
	}

	public override string ToString()
	{
		return Encoding.Latin1.GetString(Memory.Span);
	}
}
