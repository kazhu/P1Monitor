using System.Text;
using P1Monitor.Model;

namespace P1Monitor;

public record struct P1Value(ObisMapping Mapping, TrimmedMemory Data, bool IsValid, DateTimeOffset? Time = null) : IDisposable
{ 
	public readonly bool IsEmpty => Mapping == default;

	public readonly void Dispose()
	{
		Data.Dispose();
	}

	public override readonly string ToString()
	{
		return IsEmpty ? "Empty" : $"{Mapping!.Id} - {Mapping.FieldName}: {Mapping.P1Type} {Encoding.Latin1.GetString(Data.Memory.Span)} ({Mapping.Unit})";
	}
}
