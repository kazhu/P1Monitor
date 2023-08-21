using System.Text;

namespace P1Monitor;

public enum P1Type
{
	None,
	NotNeeded,
	String,
	Number,
	Time,
	OnOff,
}

public enum P1Unit
{
	None,
	kWh,
	kvarh,
	kW,
	kvar,
	Hz,
	V,
	A,
}

public readonly struct P1Value : IDisposable
{
	public readonly ObisMapping? Mapping { get; init; }
	
	public readonly TrimmedMemory Data { get; init; }
	
	public readonly bool IsValid { get; init; }
	
	public readonly DateTimeOffset? Time { get; init; }

	public readonly bool IsEmpty => Mapping == default;

	public void Dispose()
	{
		Data.Dispose();
	}

	public override string ToString()
	{
		return IsEmpty ? "Empty" : $"{Encoding.Latin1.GetString(Mapping!.Id.Memory.Span)} - {Mapping!.FieldName}: {Mapping.P1Type} {Encoding.Latin1.GetString(Data.Memory.Span)} ({Mapping.Unit})";
	}
}
