using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace P1Monitor;

public partial class DsmrReader : BackgroundService
{
	private readonly ILogger<DsmrReader> _logger;
	private readonly IInfluxDbWriter _influxDbWriter;
	private readonly DsmrReaderOptions _options;
	private readonly P1Value[] _values;
	private readonly ModbusCrc _crc = new();
	private readonly Encoding _encoding = Encoding.Latin1;
	private readonly Thread _thread;

	public enum State
	{
		Starting,
		WaitingForIdent,
		WaitingForData,
		Data,
	}

	public DsmrReader(ILogger<DsmrReader> logger, IInfluxDbWriter influxDbWriter, IOptions<DsmrReaderOptions> options)
	{
		_logger = logger;
		_influxDbWriter = influxDbWriter;
		_options = options.Value;
		_values = new P1Value[ObisMapping.Mappings.Length];
		_thread = new Thread(Run) { IsBackground = true };
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_thread.Start();
		return Task.CompletedTask;
	}

	private void Run()
	{
		var state = State.Starting;
		while (true)
		{
			try
			{
				using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				socket.ReceiveTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
				socket.Connect(_options.Host, _options.Port);
				_logger.LogInformation("Connected to {Host}:{Port}", _options.Host, _options.Port);
				byte[] buffer = ArrayPool<byte>.Shared.Rent(2048 + 16);
				try
				{
					int count = 0;
					while (true)
					{
						if (buffer.Length - count < 16)
						{
							_logger.LogWarning("Buffer full, dropping {Count} bytes of data\n{data}", count, Encoding.Latin1.GetString(buffer.AsSpan(0, count)));
							count = 0;
						}
						int bytesRead = socket.Receive(buffer, count, buffer.Length - count, SocketFlags.None);
						if (bytesRead == 0)
						{
							break;
						}
						count += bytesRead;
						ReadOnlySpan<byte> span = buffer.AsSpan(0, count);
						while (true)
						{
							int index = span.IndexOf((byte)'\n');
							if (index < 0)
							{
								if (span.Length != count)
								{
									if (span.Length > 0)
									{
										span.CopyTo(buffer);
									}
									count = span.Length;
								}
								break;
							}
							state = ProcessLine(span.Slice(0, index), state);
							span = span.Slice(index + 1);
						}
					}
				}
				finally
				{
					ArrayPool<byte>.Shared.Return(buffer);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error reading from Dsmr, retrying");
			}
		}
	}

	internal State ProcessLine(ReadOnlySpan<byte> line, State state)
	{
		// drop trailing \r
		if (line.Length > 0 && line[^1] == '\r') line = line.Slice(0, line.Length - 1);

		switch (state)
		{
			case State.Starting:
			case State.WaitingForIdent:
				if (!IsIdentLine(line))
				{
					if (state == State.Starting)
					{
						_logger.LogInformation("{Line}: dropped, waiting for ident line", _encoding.GetString(line));
					}
					else
					{
						_logger.LogError("{Line}: dropped, waiting for ident line", _encoding.GetString(line));
					}
					return state;
				}
				for (int i = 0; i < ObisMapping.Mappings.Length; i++)
				{
					_values[i].Dispose();
					_values[i] = default;
				}
				_crc.Reset();
				_crc.UpdateWithLine(line);
				return State.WaitingForData;
			case State.WaitingForData:
				if (line.Length > 0)
				{
					_logger.LogError("{Line}: dropped, expected an empty line", _encoding.GetString(line));
					return State.WaitingForIdent;
				}
				_crc.UpdateWithLine(line);
				return State.Data;
			case State.Data:
				if (IsDataLine(line))
				{
					if (ProcessData(line))
					{
						return state;
					}
				}
				else if (IsCrcLine(line))
				{
					ProcessCrc(line);
				}
				else
				{
					_logger.LogError("{Line}: dropped not a data or crc line", _encoding.GetString(line));
				}
				return State.WaitingForIdent;
			default:
				throw new InvalidOperationException($"Unknown state {state}");
		}
	}

	private bool ProcessData(ReadOnlySpan<byte> line)
	{
		_crc.UpdateWithLine(line);

		int index = line.IndexOf((byte)'(');
		using var idMemory = TrimmedMemory.Create(line.Slice(0, index));

		if (!ObisMapping.MappingById.TryGetValue(idMemory, out ObisMapping? mapping))
		{
			_logger.LogWarning("{Line}: unknown obis id, line dropped", _encoding.GetString(line));
			return true;
		}
		if (!_values[mapping.Index].IsEmpty)
		{
			_logger.LogError("{Line}: duplicated value", _encoding.GetString(line));
			return false;
		}

		_values[mapping.Index] = P1Value.Create(mapping, TrimmedMemory.Create(line.Slice(index + 1, line.Length - index - 2)));

		if (!_values[mapping.Index].IsValid)
		{
			_logger.LogError("{Line}: parsing of value failed", _encoding.GetString(line));
			return false;
		}

		if (_logger.IsEnabled(LogLevel.Trace))
		{
			var value = _values[mapping.Index];
			_logger.LogTrace("{FieldName}: {Type} {Data} ({Unit}) parsed", value.Mapping.FieldName, value.Mapping.P1Type, _encoding.GetString(value.Data.Span), value.Mapping.Unit);
		}
		return true;
	}

	private void ProcessCrc(ReadOnlySpan<byte> line)
	{
		_crc.Update((byte)'!');
		if (!_crc.IsEqual(line.Slice(1)))
		{
			_logger.LogError("{Line}: crc is invalid", _encoding.GetString(line));
			return;
		}
		for (int i = 0; i < ObisMapping.Mappings.Length; i++)
		{
			if (_values[i].IsEmpty)
			{
				_logger.LogError("{Id} is missing, dropping all values", _encoding.GetString(ObisMapping.Mappings[i].Id.Span));
				return;
			}
		}
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Enqueuing values for {Time}", _values[ObisMapping.MappingByFieldName["time"].Index].Time);
		}
		_influxDbWriter.Insert(_values);
	}

	private bool IsIdentLine(ReadOnlySpan<byte> line)
	{
		// @"^/...5[0-9]+\z"
		if (line.Length < 6 || line[0] != '/' || line[4] != '5') return false;
		for (int i = 5; i < line.Length; i++)
			if (!IsDigit(line[i])) return false;
		return true;
	}

	private bool IsDataLine(ReadOnlySpan<byte> line)
	{
		// @"^[0-9]-[0-9]:[0-9]+\.[0-9]+\.[0-9]+\(.+\)\z"
		if (!(line.Length > 4 && IsDigit(line[0]) && line[1] == '-' && IsDigit(line[2]) && line[3] == ':')) return false;
		int index = 4;

		if (!(index < line.Length && IsDigit(line[index++]))) return false;
		while (index < line.Length && IsDigit(line[index])) index++;

		if (!(index < line.Length && line[index++] == '.')) return false;

		if (!(index < line.Length && IsDigit(line[index++]))) return false;
		while (index < line.Length && IsDigit(line[index])) index++;

		if (!(index < line.Length && line[index++] == '.')) return false;

		if (!(index < line.Length && IsDigit(line[index++]))) return false;
		while (index < line.Length && IsDigit(line[index])) index++;

		if (!(index < line.Length && line[index++] == '(')) return false;

		return index < line.Length - 1 && line[line.Length - 1] == ')';
	}

	private bool IsCrcLine(ReadOnlySpan<byte> line)
	{
		// @"^![0-9A-F]{4}\z"
		return line.Length == 5 && line[0] == '!' && IsHexDigit(line[1]) && IsHexDigit(line[2]) && IsHexDigit(line[3]) && IsHexDigit(line[4]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsDigit(byte value) => value >= '0' && value <= '9';

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsHexDigit(byte value) => IsDigit(value) || (value >= 'A' && value <= 'F');
}
