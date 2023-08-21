using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Text;

namespace P1Monitor;

public partial class DsmrReader : BackgroundService
{
	private static readonly Encoding _encoding = Encoding.Latin1;

	private readonly ILogger<DsmrReader> _logger;
	private readonly IInfluxDbWriter _influxDbWriter;
	private readonly DsmrParser _dsmrParser;
	private readonly DsmrReaderOptions _options;
	private readonly P1Value[] _values = new P1Value[ObisMapping.Mappings.Length];
	private Socket? _socket = null!;

	public DsmrReader(ILogger<DsmrReader> logger, IInfluxDbWriter influxDbWriter, DsmrParser dsmrParser, IOptions<DsmrReaderOptions> options)
	{
		_logger = logger;
		_influxDbWriter = influxDbWriter;
		_dsmrParser = dsmrParser;
		_options = options.Value;
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		return Task.Run(() => Run(stoppingToken), stoppingToken);
	}

	override public Task StopAsync(CancellationToken cancellationToken)
	{
		Task task = base.StopAsync(cancellationToken);
		_socket?.Dispose();
		return task;
	}

	private void Run(CancellationToken stoppingToken)
	{
		Span<byte> buffer = stackalloc byte[4096];
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				try
				{
					_socket.ReceiveTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
					_socket.Connect(_options.Host, _options.Port);
					_logger.LogInformation("Connected to {Host}:{Port}", _options.Host, _options.Port);
					int count = 0;
					while (!stoppingToken.IsCancellationRequested)
					{
						if (buffer.Length - count < 16)
						{
							_logger.LogError("Buffer full, dropping {Count} bytes of data\n{data}", count, Encoding.Latin1.GetString(buffer[..count]));
							count = 0;
						}
						int bytesRead = _socket.Receive(buffer[count..], SocketFlags.None);
						if (bytesRead == 0)
						{
							break;
						}
						count = ProcessBuffer(buffer[..(count + bytesRead)]);
					}
				}
				finally
				{
					_socket.Dispose();
				}
			}
			catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
			{
				_logger.LogError(ex, "Error reading from Dsmr, retrying");
			}
			catch
			{
				// Ignore, becase we are stopping
			}
		}
		_logger.LogInformation("DSMR reader stopped");
	}

	internal int ProcessBuffer(Span<byte> originalBuffer)
	{
		ReadOnlySpan<byte> buffer = originalBuffer;
		while (_dsmrParser.TryFindDataLines(ref buffer, out ReadOnlySpan<byte> dataLines))
		{
			ProcessDataLines(dataLines);
		}
		if (buffer.Length > 0 && originalBuffer.Length != buffer.Length)
		{
			buffer.CopyTo(originalBuffer);
		}
		return buffer.Length;
	}

	private void ProcessDataLines(ReadOnlySpan<byte> span)
	{
		bool hasError = false;
		while (!span.IsEmpty)
		{
			if (_dsmrParser.TryParseDataLine(ref span, out var value))
			{
				if (!_values[value.Mapping!.Index].IsEmpty)
				{
					_logger.LogError("{Value}: duplicated value", value.ToString());
					value.Dispose();
					hasError = true;
					continue;
				}

				if (_logger.IsEnabled(LogLevel.Trace))
				{
					_logger.LogTrace("{Value} parsed", value.ToString());
				}
				_values[value.Mapping!.Index] = value;
			}
		}

		for (int i = 0; i < _values.Length; i++)
		{
			if (_values[i].IsEmpty)
			{
				_logger.LogError("{Id} is missing, dropping all values", _encoding.GetString(ObisMapping.Mappings[i].Id.Memory.Span));
				hasError = true;
			}
		}

		if (!hasError)
		{
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug("Enqueuing values for {Time}", _values[ObisMapping.MappingByFieldName["time"].Index].Time);
			}
			_influxDbWriter.Insert(_values);
		}

		for (int i = 0; i < _values.Length; i++)
		{
			_values[i].Dispose();
			_values[i] = default;
		}
	}
}
