using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace P1Monitor;

public partial class DsmrReader : BackgroundService
{
	private readonly ILogger<DsmrReader> _logger;
	private readonly ChannelWriter<List<P1Value>> _valuesWriter;
	private readonly DsmrReaderOptions _options;
	private readonly List<P1Value> _values = new();
	private readonly ModbusCrc _crc = new();

	private static readonly ObisMapping[] ObisMappings = new[]
	{
		P1TimeValue.GetMapping("0-0:1.0.0", "time"),
		P1StringValue.GetMapping("0-0:42.0.0", "name"),
		P1StringValue.GetMapping("0-0:96.1.0", "serial"),
		P1NumberValue.GetMapping("0-0:96.14.0", "tariff", P1Unit.None),
		P1OnOffValue.GetMapping("0-0:96.50.68", "state"),
		P1NumberValue.GetMapping("1-0:1.8.0", "import_energy", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:1.8.1", "import_energy_tariff_1", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:1.8.2", "import_energy_tariff_2", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:1.8.3", "import_energy_tariff_3", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:1.8.4", "import_energy_tariff_4", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:2.8.0", "export_energy", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:2.8.1", "export_energy_tariff_1", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:2.8.2", "export_energy_tariff_2", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:2.8.3", "export_energy_tariff_3", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:2.8.4", "export_energy_tariff_4", P1Unit.kWh),
		P1NumberValue.GetMapping("1-0:3.8.0", "import_reactive_energy", P1Unit.kvarh),
		P1NumberValue.GetMapping("1-0:4.8.0", "export_reactive_energy", P1Unit.kvarh),
		P1NumberValue.GetMapping("1-0:5.8.0", "reactive_energy_q1", P1Unit.kvarh),
		P1NumberValue.GetMapping("1-0:6.8.0", "reactive_energy_q2", P1Unit.kvarh),
		P1NumberValue.GetMapping("1-0:7.8.0", "reactive_energy_q3", P1Unit.kvarh),
		P1NumberValue.GetMapping("1-0:8.8.0", "reactive_energy_q4", P1Unit.kvarh),
		P1NumberValue.GetMapping("1-0:32.7.0", "voltage_l1", P1Unit.V),
		P1NumberValue.GetMapping("1-0:52.7.0", "voltage_l2", P1Unit.V),
		P1NumberValue.GetMapping("1-0:72.7.0", "voltage_l3", P1Unit.V),
		P1NumberValue.GetMapping("1-0:31.7.0", "current_l1", P1Unit.A),
		P1NumberValue.GetMapping("1-0:51.7.0", "current_l2", P1Unit.A),
		P1NumberValue.GetMapping("1-0:71.7.0", "current_l3", P1Unit.A),
		P1NumberValue.GetMapping("1-0:13.7.0", "power_factor", P1Unit.None),
		P1NumberValue.GetMapping("1-0:33.7.0", "power_factor_l1", P1Unit.None),
		P1NumberValue.GetMapping("1-0:53.7.0", "power_factor_l2", P1Unit.None),
		P1NumberValue.GetMapping("1-0:73.7.0", "power_factor_l3", P1Unit.None),
		P1NumberValue.GetMapping("1-0:14.7.0", "frequency", P1Unit.Hz),
		P1NumberValue.GetMapping("1-0:1.7.0", "import_power", P1Unit.kW),
		P1NumberValue.GetMapping("1-0:2.7.0", "export_power", P1Unit.kW),
		P1NumberValue.GetMapping("1-0:5.7.0", "reactive_power_q1", P1Unit.kvar),
		P1NumberValue.GetMapping("1-0:6.7.0", "reactive_power_q2", P1Unit.kvar),
		P1NumberValue.GetMapping("1-0:7.7.0", "reactive_power_q3", P1Unit.kvar),
		P1NumberValue.GetMapping("1-0:8.7.0", "reactive_power_q4", P1Unit.kvar),
		P1NoValue.GetMapping("0-0:17.0.0", "limiter_limit"),
		P1NoValue.GetMapping("1-0:15.8.0", "energy_combined"),
		P1NoValue.GetMapping("1-0:31.4.0", "current_limit_l1"),
		P1NoValue.GetMapping("1-0:51.4.0", "current_limit_l2"),
		P1NoValue.GetMapping("1-0:71.4.0", "current_limit_l3"),
		P1NoValue.GetMapping("0-0:98.1.0", "previous_month"),
		P1NoValue.GetMapping("0-0:96.13.0", "message")
	};

	private static readonly Dictionary<string, ObisMapping> ObisMappingsById = ObisMappings.ToDictionary(m => m.Id);


	private enum State
	{
		Starting,
		WaitingForIdent,
		WaitingForData,
		Data,
	}

	public DsmrReader(ILogger<DsmrReader> logger, ChannelWriter<List<P1Value>> valuesWriter, IOptions<DsmrReaderOptions> options)
	{
		_logger = logger;
		_valuesWriter = valuesWriter;
		_options = options.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (true)
		{
			try
			{
				using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				await socket.ConnectAsync(_options.Host, _options.Port, stoppingToken);
				using var reader = new StreamReader(new NetworkStream(socket), Encoding.Latin1, detectEncodingFromByteOrderMarks: false);
				State state = State.Starting;
				while (true)
				{
					using CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
					linkedTokenSource.CancelAfter(TimeSpan.FromMinutes(1));
					string? line = await reader.ReadLineAsync(linkedTokenSource.Token);
					if (line == null) break;
					state = ProcessLine(line, state);
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("DsmrReader stopped");
				return;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error reading from Dsmr, retrying");
			}
		}
	}

	private State ProcessLine(string line, State state)
	{
		switch (state)
		{
			case State.Starting:
			case State.WaitingForIdent:
				if (!GetIdentRegex().IsMatch(line))
				{
					if (state == State.Starting)
					{
						_logger.LogInformation("{Line}: dropped, waiting for ident line", line);
					}
					else
					{
						_logger.LogError("{Line}: dropped", line);
					}
					return state;
				}
				_values.Clear();
				_crc.Reset();
				_crc.UpdateWithLine(line);
				return State.WaitingForData;
			case State.WaitingForData:
				if (line != "")
				{
					_logger.LogError("{Line}: dropped", line);
					return State.WaitingForIdent;
				}
				_crc.UpdateWithLine(line);
				return State.Data;
			case State.Data:
				Match dataMatch = GetDataLineRegex().Match(line);
				if (dataMatch.Success)
				{
					_crc.UpdateWithLine(line);
					ProcessData(line, dataMatch);
					return State.Data;
				}
				Match crcMatch = GetCrcLineRegex().Match(line);
				if (crcMatch.Success)
				{
					_crc.Update('!');
					ProcessCrc(line, crcMatch);
				}
				else
				{
					_logger.LogError("{Line}: dropped", line);
				}
				return State.WaitingForIdent;
			default:
				throw new InvalidOperationException($"Unknown state {state}");
		}
	}

	private void ProcessData(string line, Match dataMatch)
	{
		string id = dataMatch.Groups["id"].Value;
		if (!ObisMappingsById.TryGetValue(id, out ObisMapping? mapping) || mapping == null)
		{
			_logger.LogWarning("{Line}: unknown obis id, line dropped", line);
			return;
		}
		P1Value value = mapping.CreateValue(dataMatch.Groups["data"].Value);
		if (!value.IsValid)
		{
			_logger.LogError("{Line}: parsing of value failed", line);
			return;
		}
		_values.Add(value);
		_logger.LogTrace("{Value} parsed", value);
	}

	private void ProcessCrc(string line, Match crcMatch)
	{
		if (crcMatch.Groups["crc"].Value != _crc.GetCrc())
		{
			_logger.LogError("{Line}: crc is invalid", line);
			return;
		}
		if (!_valuesWriter.TryWrite(_values.ToList()))
		{
			_logger.LogWarning("{Count} values was dropped, because channel is full", _values.Count);
			return;
		}
		_logger.LogDebug("{Count} values enqueued", _values.Count);
	}

	[GeneratedRegex(@"^/...5\d+\z")]
	private static partial Regex GetIdentRegex();

	[GeneratedRegex(@"^(?<id>[01]-0:\d+.\d+.\d+)(?<datalist>\((?<data>.+)\))+\z")]
	private static partial Regex GetDataLineRegex();

	[GeneratedRegex(@"^!(?<crc>[0-9A-F]{4})\z")]
	private static partial Regex GetCrcLineRegex();
}
