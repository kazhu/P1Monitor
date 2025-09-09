using Microsoft.Extensions.Logging;
using P1Monitor.Model;
using System.Text;

namespace P1Monitor;

public interface IDsmrParser
{
	bool TryFindDataLines(ref ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> dataLines);
	DsmrValue? ParseDataLine(ref ReadOnlySpan<byte> buffer, DsmrValue[] values);
}

public class DsmrParser(ILogger<DsmrParser> logger, IObisMappingsProvider obisMappingProvider) : IDsmrParser
{
	private static readonly Encoding _encoding = Encoding.Latin1;
    private bool isFirstDatagram = true;

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
				logger.LogInformation("Dropped data before datagram start: {Data}", _encoding.GetString(buffer[..(packetStartIndex + 2)]));
			}
			else
			{
				logger.LogError("Dropped data before datagram start: {Data}", _encoding.GetString(buffer[..(packetStartIndex + 2)]));
			}
			buffer = buffer[(packetStartIndex + 2)..];
		}

		// check ident line
		int identLineEndIndex = buffer.IndexOf("\r\n"u8);
		if (identLineEndIndex < 0 || identLineEndIndex + 3 >= buffer.Length) return false;
		if (buffer[4] != '5' || buffer[identLineEndIndex + 2] != '\r' || buffer[identLineEndIndex + 3] != '\n')
		{
			logger.LogError("Invalid identification line, dropped the line {Line}", _encoding.GetString(buffer[..identLineEndIndex]));
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
		if (buffer[index + 4] == '\r' && buffer[index + 5] == '\n' && DsmrCrc.CheckCrc(buffer[..index], buffer.Slice(index, 4)))
		{
			dataLines = buffer.Slice(dataStartIndex, dataLength);
			buffer = buffer[(index + 6)..];
			return true;
		}

		// invalid crc
		logger.LogError("Invalid CRC, dropped the datagram {Datagram}", _encoding.GetString(buffer[..(index + 6)]));
		buffer = buffer[(index + 6)..];
		return false;
	}

	public DsmrValue? ParseDataLine(ref ReadOnlySpan<byte> buffer, DsmrValue[] values)
	{
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
			logger.LogError("{Line}: not well formed, dropped", _encoding.GetString(line));
			return null;
		}
		var valueSpan = line.Slice(index + 1, line.Length - index - 2);

		if (!obisMappingProvider.Mappings.TryGetMappingById(line[..index], out ObisMapping? mapping))
		{
			logger.LogWarning("{Line}: unknown obis id, line dropped", _encoding.GetString(line));
			return null;
		}

		DsmrValue value = values[mapping!.Index];
		if (!value.IsEmpty)
		{
			logger.LogError("{Line}: duplicated value", _encoding.GetString(line));
			return DsmrValue.Error;
		}

		if (!value.TrySetValue(valueSpan))
		{
			logger.LogError("{Line}: parsing of value failed", _encoding.GetString(line));
			return value;
		}

		if (logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("{Value} parsed", value.ToString());
		}
		return value;
	}
}
