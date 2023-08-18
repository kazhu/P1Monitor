using System.Buffers;

namespace P1Monitor;

public enum P1Type
{
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

public partial record struct P1Value(ObisMapping Mapping, TrimmedMemory Data, bool IsValid = true, DateTimeOffset? Time = null) : IDisposable
{
	public readonly bool IsEmpty => this == default;

	public static P1Value Create(ObisMapping mapping, TrimmedMemory memory)
	{
		switch (mapping.P1Type)
		{
			case P1Type.NotNeeded:
				return new P1Value(mapping, memory);
			case P1Type.String:
				return new P1Value(mapping, memory, memory.Length < 32);
			case P1Type.Number:
				if (TryParseNumber(memory, mapping.Unit, out var number))
					return new P1Value(mapping, number, true);
				return new P1Value(mapping, memory, false);
			case P1Type.Time:
				return new P1Value(mapping, memory, TryParseTime(memory.Span, out var time), time);
			case P1Type.OnOff:
				return new P1Value(mapping, memory, IsOnOff(memory.Span));
			default:
				throw new ArgumentException($"Unknown P1Type {mapping.P1Type}");
		}
	}

	public void Dispose()
	{
		if (!IsEmpty)
		{
			Data.Dispose();
		}
	}

	private static class Constants
	{
		public static readonly byte[] kWh = "*kWh".Select(x => (byte)x).ToArray();
		public static readonly byte[] kvarh = "*kvarh".Select(x => (byte)x).ToArray();
		public static readonly byte[] kW = "*kW".Select(x => (byte)x).ToArray();
		public static readonly byte[] kvar = "*kvar".Select(x => (byte)x).ToArray();
		public static readonly byte[] Hz = "*Hz".Select(x => (byte)x).ToArray();
		public static readonly byte[] V = "*V".Select(x => (byte)x).ToArray();
		public static readonly byte[] A = "*A".Select(x => (byte)x).ToArray();
		public static readonly byte[] On = "ON".Select(x => (byte)x).ToArray();
		public static readonly byte[] Off = "OFF".Select(x => (byte)x).ToArray();
	}

	private static bool TryParseNumber(TrimmedMemory memory, P1Unit unit, out TrimmedMemory number)
	{
		number = memory;
		int separatorIndex = number.Span.IndexOf((byte)'*');
		if (separatorIndex >= 0)
		{
			if (!number.Span.Slice(separatorIndex).SequenceEqual(GetUnitBytes(unit))) return false;
			number = number.Slice(0, separatorIndex);
		}
		number = TrimZerosForNumber(number);

		return IsNumber(number.Span);
	}

	private static bool TryParseTime(ReadOnlySpan<byte> span, out DateTimeOffset? time)
	{
		time = null;
		if (span.Length != 13 || span[12] != 'S') return false;
		for (int i = 0; i < 12; i++) if (span[i] < '0' || span[i] > '9') return false;
		int year = 2000 + Get2DigitsValue(span, 0);
		int month = Get2DigitsValue(span, 2);
		int day = Get2DigitsValue(span, 4);
		int hour = Get2DigitsValue(span, 6);
		int minute = Get2DigitsValue(span, 8);
		int second = Get2DigitsValue(span, 10);
		if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59)
		{
			return false;
		}
		try
		{
			time = new DateTimeOffset(year, month, day, hour, minute, second, DateTimeOffset.Now.Offset); // it is not perfect, because of DST may effect the result
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static Span<byte> GetUnitBytes(P1Unit unit)
	{
		return unit switch
		{
			P1Unit.kWh => Constants.kWh,
			P1Unit.kvarh => Constants.kvarh,
			P1Unit.kW => Constants.kW,
			P1Unit.kvar => Constants.kvar,
			P1Unit.Hz => Constants.Hz,
			P1Unit.V => Constants.V,
			P1Unit.A => Constants.A,
			_ => Span<byte>.Empty,
		};
	}

	private static bool IsOnOff(ReadOnlySpan<byte> span)
	{
		return span.SequenceEqual(Constants.On) || span.SequenceEqual(Constants.Off);
	}

	private static bool IsNumber(ReadOnlySpan<byte> value)
	{
		int dotIndex = value.IndexOf((byte)'.');
		for (int i = 0; i < value.Length; i++)
		{
			// disallow non digits except in the dot position
			if ((value[i] < '0' || value[i] > '9') && i != dotIndex) return false;
		}
		// valid if dot is not the first or last character and not too long
		return dotIndex != 0 && dotIndex != value.Length - 1 && value.Length < 15;
	}

	internal static TrimmedMemory TrimZerosForNumber(TrimmedMemory memory)
	{
		int start = 0, end = memory.Length;
		if (start == end) return memory;
		while (start < end && memory.Span[start] == '0') start++; // drop leading zeros
		while (start < end && memory.Span[end - 1] == '0') end--; // drop trailing zeros
		if (start == end) start = end - 1; // keep at least one zero
		if (start > 0 && memory.Span[start] == '.') start--;  // if all leading zeros were dropped, add one back
		if (end < memory.Length && memory.Span[end - 1] == '.') end++; // if all trailing zeros were dropped, add one back
		return memory.Slice(start, end - start);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Get2DigitsValue(ReadOnlySpan<byte> span, int index) => (span[index] - '0') * 10 + (span[index + 1] - '0');
}
