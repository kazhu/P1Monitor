using Microsoft.Extensions.Logging;
using System.Text;

namespace P1Monitor;

public class DsmrParser
{
	private static readonly Encoding _encoding = Encoding.Latin1;
	private readonly ILogger<DsmrParser> _logger;
	private bool isFirstDatagram = true;

	public DsmrParser(ILogger<DsmrParser> logger)
	{
		_logger = logger;
	}

	// looking for /XXX5<identification>\r\n\r\n<dataLines>\r\n!<crc>\r\n where data lines are separated by \r\n and cannot contain \r\n
	public bool TryFindDataLines(ref ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> dataLines)
	{
		dataLines = default;
		if (buffer.IsEmpty) return false;

		// if we don't start with the ident line, try to find it and drop leading garbage
		if (buffer[0] != '/')
		{
			int packetStartIndex = buffer.IndexOf("\r\n/"u8);
			if (packetStartIndex < 0) return false;
			if (isFirstDatagram)
			{
				_logger.LogInformation("Dropped data before datagramm start: {Data}", _encoding.GetString(buffer[..(packetStartIndex + 2)]));
			}
			else
			{
				_logger.LogError("Dropped data before datagramm start: {Data}", _encoding.GetString(buffer[..(packetStartIndex + 2)]));
			}
			buffer = buffer[(packetStartIndex + 2)..];
		}

		// check ident line
		int identLineEndIndex = buffer.IndexOf("\r\n"u8);
		if (identLineEndIndex < 0 || identLineEndIndex + 3 >= buffer.Length) return false;
		if (buffer[4] != '5' || buffer[identLineEndIndex + 2] != '\r' || buffer[identLineEndIndex + 3] != '\n')
		{
			_logger.LogError("Invalid identification line, dropped the line {Line}", _encoding.GetString(buffer[..identLineEndIndex]));
			buffer = buffer[(identLineEndIndex + 2)..];
			return false;
		}
		int dataStartIndex = identLineEndIndex + 4;

		// looking for crc line
		int index = buffer.IndexOf("\r\n!"u8);
		if (index < 0) return false;
		int dataLength = index - dataStartIndex;

		// checking crc
		index += 3;
		if (index + 5 >= buffer.Length) return false;
		isFirstDatagram = false;
		if (buffer[index + 4] == '\r' && buffer[index + 5] == '\n' && ModbusCrc.CheckCrc(buffer[..index], buffer.Slice(index, 4)))
		{
			dataLines = buffer.Slice(dataStartIndex, dataLength);
			buffer = buffer[(index + 6)..];
			return true;
		}

		// invalid crc
		_logger.LogError("Invalid CRC, dropped the datagramm {Datagramm}", _encoding.GetString(buffer[..(index + 6)]));
		buffer = buffer[(index + 6)..];
		return false;
	}

	public bool TryParseDataLine(ref ReadOnlySpan<byte> buffer, out P1Value value)
	{
		value = default;
		int index = buffer.IndexOf("\r\n"u8);
		ReadOnlySpan<byte> line;
		if (index >= 0)
		{
			line = buffer[..index];
			buffer = buffer[(index + 2)..];
		}
		else
		{
			line = buffer;
			buffer = default;
		}

		index = line.IndexOf((byte)'(');
		if (index < 1 || line[^1] != ')')
		{
			_logger.LogError("{Line}: not well formed, dropped", _encoding.GetString(line));
			return false;
		}

		using var id = new TrimmedMemory(line[..index]);
		if (!ObisMapping.MappingById.TryGetValue(id, out ObisMapping? mapping))
		{
			_logger.LogWarning("{Line}: unknown obis id, line dropped", _encoding.GetString(line));
			return false;
		}

		var valueSpan = line.Slice(index + 1, line.Length - index - 2);
		P1Value p1Value = mapping.P1Type switch
		{
			P1Type.NotNeeded => new P1Value { Mapping = mapping, Data = new TrimmedMemory(valueSpan), IsValid = true, Time = null },
			P1Type.String => new P1Value { Mapping = mapping, Data = new TrimmedMemory(valueSpan), IsValid = valueSpan.Length <= 32, Time = null },
			P1Type.Number => TryParseNumber(valueSpan, mapping.Unit, out var number)
				? new P1Value { Mapping = mapping, Data = new TrimmedMemory(number), IsValid = true, Time = null }
				: new P1Value { Mapping = mapping, Data = new TrimmedMemory(valueSpan), IsValid = false, Time = null },
			P1Type.Time => new P1Value { Mapping = mapping, Data = new TrimmedMemory(valueSpan), IsValid = TryParseTime(valueSpan, out var time), Time = time },
			P1Type.OnOff => new P1Value { Mapping = mapping, Data = new TrimmedMemory(valueSpan), IsValid = IsOnOff(valueSpan), Time = null },
			_ => throw new NotImplementedException(),
		};
		if (!p1Value.IsValid)
		{
			p1Value.Dispose();
			_logger.LogError("{Line}: parsing of value failed", _encoding.GetString(line));
			return false;
		}

		value = p1Value;
		return true;
	}

	public static bool TryParseNumber(ReadOnlySpan<byte> span, P1Unit unit, out ReadOnlySpan<byte> number)
	{
		int separatorIndex = span.IndexOf((byte)'*');
		if (separatorIndex >= 0)
		{
			ReadOnlySpan<byte> expectedUnitText = unit switch
			{
				P1Unit.kWh => "*kWh"u8,
				P1Unit.kvarh => "*kvarh"u8,
				P1Unit.kW => "*kW"u8,
				P1Unit.kvar => "*kvar"u8,
				P1Unit.Hz => "*Hz"u8,
				P1Unit.V => "*V"u8,
				P1Unit.A => "*A"u8,
				_ => Span<byte>.Empty,
			};
			if (!span[separatorIndex..].SequenceEqual(expectedUnitText))
			{
				number = default;
				return false;
			}
			number = TrimZerosForNumber(span[..separatorIndex]);
		}
		else
		{
			if (unit != P1Unit.None)
			{
				number = default;
				return false;
			}
			number = TrimZerosForNumber(span);
		}

		return IsNumber(number);
	}

	public static bool TryParseTime(ReadOnlySpan<byte> span, out DateTimeOffset? time)
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

	private static bool IsOnOff(ReadOnlySpan<byte> span)
	{
		return span.SequenceEqual("ON"u8) || span.SequenceEqual("OFF"u8);
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

	private static ReadOnlySpan<byte> TrimZerosForNumber(ReadOnlySpan<byte> span)
	{
		int start = 0, end = span.Length;
		if (start == end) return span;
		while (start < end && span[start] == '0') start++; // drop leading zeros
		while (start < end && span[end - 1] == '0') end--; // drop trailing zeros
		if (start == end) start = end - 1; // keep at least one zero
		if (start > 0 && span[start] == '.') start--;  // if all leading zeros were dropped, add one back
		if (end < span.Length && span[end - 1] == '.') end++; // if all trailing zeros were dropped, add one back
		return span[start..end];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Get2DigitsValue(ReadOnlySpan<byte> span, int index) => (span[index] - '0') * 10 + (span[index + 1] - '0');
}
