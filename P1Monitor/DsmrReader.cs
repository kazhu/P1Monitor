using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.IO.Pipelines;
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
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var pipe = new Pipe();
		Task socketReaderTask = ReadFromSocketAsync(pipe.Writer, stoppingToken);
		Task lineProcessingTask = ProcessDataAsync(pipe.Reader, stoppingToken);

		await Task.WhenAll(socketReaderTask, lineProcessingTask);
	}

	private async Task ReadFromSocketAsync(PipeWriter writer, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				await socket.ConnectAsync(_options.Host, _options.Port, cancellationToken);
				_logger.LogInformation("Connected to {Host}:{Port}", _options.Host, _options.Port);
				while (!cancellationToken.IsCancellationRequested)
				{
					Memory<byte> memory = writer.GetMemory(512);
					using (var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
					{
						linkedTokenSource.CancelAfter(TimeSpan.FromMinutes(1));
						int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None, linkedTokenSource.Token);
						if (bytesRead == 0)
						{
							break;
						}
						writer.Advance(bytesRead);
					}
					FlushResult result = await writer.FlushAsync(cancellationToken);
					if (result.IsCompleted)
					{
						break;
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				_logger.LogInformation("DsmrReader stopped");
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error reading from Dsmr, retrying");
			}
		}
		await writer.CompleteAsync();
	}

	private async Task ProcessDataAsync(PipeReader reader, CancellationToken cancellationToken)
	{
		State state = State.Starting;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				ReadResult result = await reader.ReadAsync(cancellationToken);
				ReadOnlySequence<byte> buffer = result.Buffer;
				while (true)
				{
					SequencePosition? position = buffer.PositionOf((byte)'\n');
					if (position == null)
					{
						break;
					}
					int length = (int)buffer.GetOffset(position.Value);
					if (length > 0)
					{
						using var lineMemory = TrimmedMemory.Create(length);
						buffer.Slice(0, position.Value).CopyTo(lineMemory.Span);
						await ProcessLine(lineMemory.Span, ref state, cancellationToken);
					}
					buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
					reader.AdvanceTo(buffer.Start, buffer.End);
				}
				if (result.IsCompleted)
				{
					break;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error while processing data");
			}
		}
		await reader.CompleteAsync();
	}

	internal Task ProcessLine(ReadOnlySpan<byte> line, ref State state, CancellationToken cancellationToken)
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
					return Task.CompletedTask;
				}
				for (int i = 0; i < ObisMapping.Mappings.Length; i++)
				{
					_values[i].Dispose();
					_values[i] = default;
				}
				_crc.Reset();
				_crc.UpdateWithLine(line);
				state = State.WaitingForData;
				return Task.CompletedTask;
			case State.WaitingForData:
				if (line.Length > 0)
				{
					_logger.LogError("{Line}: dropped, expected an empty line", _encoding.GetString(line));
					state = State.WaitingForIdent;
					return Task.CompletedTask;
				}
				_crc.UpdateWithLine(line);
				state = State.Data;
				return Task.CompletedTask;
			case State.Data:
				if (IsDataLine(line))
				{
					if (ProcessData(line))
					{
						return Task.CompletedTask;
					}
				}
				else if (IsCrcLine(line))
				{
					Task task = ProcessCrc(line, cancellationToken);
					state = State.WaitingForIdent;
					return task;
				}
				else
				{
					_logger.LogError("{Line}: dropped not a data or crc line", _encoding.GetString(line));
				}
				state = State.WaitingForIdent;
				return Task.CompletedTask;
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

	private Task ProcessCrc(ReadOnlySpan<byte> line, CancellationToken cancellationToken)
	{
		_crc.Update((byte)'!');
		if (!_crc.IsEqual(line.Slice(1)))
		{
			_logger.LogError("{Line}: crc is invalid", _encoding.GetString(line));
			return Task.CompletedTask;
		}
		for (int i = 0; i < ObisMapping.Mappings.Length; i++)
		{
			if (_values[i].IsEmpty)
			{
				_logger.LogError("{Id} is missing, dropping all values", _encoding.GetString(ObisMapping.Mappings[i].Id.Span));
				return Task.CompletedTask;
			}
		}
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Enqueuing values for {Time}", _values[ObisMapping.MappingByFieldName["time"].Index].Time);
		}
		return _influxDbWriter.InsertAsync(_values, cancellationToken);
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
