using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P1Monitor.Model;
using P1Monitor.Options;
using System.Net.Sockets;
using System.Text;

namespace P1Monitor;

public partial class DsmrReader : BackgroundService
{
	private static readonly Encoding _encoding = Encoding.Latin1;

	private readonly ILogger<DsmrReader> _logger;
	private readonly IInfluxDbWriter _influxDbWriter;
	private readonly IDsmrParser _dsmrParser;
	private readonly IObisMappingsProvider _obisMappingProvider;
	private readonly DsmrReaderOptions _options;
	private readonly DsmrValue[] _values;
	private Socket? _socket = null!;

	public DsmrReader(ILogger<DsmrReader> logger, IInfluxDbWriter influxDbWriter, IDsmrParser dsmrParser, IObisMappingsProvider obisMappingProvider, IOptions<DsmrReaderOptions> options)
	{
		_logger = logger;
		_influxDbWriter = influxDbWriter;
		_dsmrParser = dsmrParser;
		_obisMappingProvider = obisMappingProvider;
		_options = options.Value;
		_values = new DsmrValue[_obisMappingProvider.Mappings.Count];
		foreach (ObisMapping mapping in _obisMappingProvider.Mappings)
		{
			_values[mapping.Index] = DsmrValue.Create(mapping);
		}
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
		Span<byte> buffer = _options.BufferSize <= 4096 ? stackalloc byte[_options.BufferSize] : new byte[_options.BufferSize];
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
		foreach (DsmrValue value in _values) value.Clear();

		while (!span.IsEmpty)
		{
			_dsmrParser.ParseDataLine(ref span, _values);
		}

		bool hasError = false;
		for (int i = 0; i < _values.Length; i++)
		{
			if (_values[i].IsEmpty)
			{
				_logger.LogError("{Id} is missing, dropping all values", _obisMappingProvider.Mappings[i].Id);
				hasError = true;
			}
		}

		if (!hasError)
		{
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug("Enqueuing values for {Time}", (_obisMappingProvider.Mappings.TimeField is null ? null : ((DsmrTimeValue)_values[_obisMappingProvider.Mappings.TimeField.Index]).Value) ?? DateTimeOffset.Now);
			}
			_influxDbWriter.Insert(_values);
		}

		foreach (DsmrValue value in _values) value.Clear();
	}
}
