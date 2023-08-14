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
		P1NumberValue.GetMapping("0-0:96.14.0", "tariff"),
		P1OnOffValue.GetMapping("0-0:96.50.68", "state"),
		P1NumberValue.GetMapping("1-0:1.8.0", "import_energy"),
		P1NumberValue.GetMapping("1-0:1.8.1", "import_energy_tariff_1"),
		P1NumberValue.GetMapping("1-0:1.8.2", "import_energy_tariff_2"),
		P1NumberValue.GetMapping("1-0:1.8.3", "import_energy_tariff_3"),
		P1NumberValue.GetMapping("1-0:1.8.4", "import_energy_tariff_4"),
		P1NumberValue.GetMapping("1-0:2.8.0", "export_energy"),
		P1NumberValue.GetMapping("1-0:2.8.1", "export_energy_tariff_1"),
		P1NumberValue.GetMapping("1-0:2.8.2", "export_energy_tariff_2"),
		P1NumberValue.GetMapping("1-0:2.8.3", "export_energy_tariff_3"),
		P1NumberValue.GetMapping("1-0:2.8.4", "export_energy_tariff_4"),
		P1NumberValue.GetMapping("1-0:3.8.0", "import_reactive_energy"),
		P1NumberValue.GetMapping("1-0:4.8.0", "export_reactive_energy"),
		P1NumberValue.GetMapping("1-0:5.8.0", "reactive_energy_q1"),
		P1NumberValue.GetMapping("1-0:6.8.0", "reactive_energy_q2"),
		P1NumberValue.GetMapping("1-0:7.8.0", "reactive_energy_q3"),
		P1NumberValue.GetMapping("1-0:8.8.0", "reactive_energy_q4"),
		P1NumberValue.GetMapping("1-0:32.7.0", "voltage_l1"),
		P1NumberValue.GetMapping("1-0:52.7.0", "voltage_l2"),
		P1NumberValue.GetMapping("1-0:72.7.0", "voltage_l3"),
		P1NumberValue.GetMapping("1-0:31.7.0", "current_l1"),
		P1NumberValue.GetMapping("1-0:51.7.0", "current_l2"),
		P1NumberValue.GetMapping("1-0:71.7.0", "current_l3"),
		P1NumberValue.GetMapping("1-0:13.7.0", "power_factor"),
		P1NumberValue.GetMapping("1-0:33.7.0", "power_factor_l1"),
		P1NumberValue.GetMapping("1-0:53.7.0", "power_factor_l2"),
		P1NumberValue.GetMapping("1-0:73.7.0", "power_factor_l3"),
		P1NumberValue.GetMapping("1-0:14.7.0", "frequency"),
		P1NumberValue.GetMapping("1-0:1.7.0", "import_power"),
		P1NumberValue.GetMapping("1-0:2.7.0", "export_power"),
		P1NumberValue.GetMapping("1-0:5.7.0", "reactive_power_q1"),
		P1NumberValue.GetMapping("1-0:6.7.0", "reactive_power_q2"),
		P1NumberValue.GetMapping("1-0:7.7.0", "reactive_power_q3"),
		P1NumberValue.GetMapping("1-0:8.7.0", "reactive_power_q4"),
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
				for (string? line = await reader.ReadLineAsync(stoppingToken); line != null; line = await reader.ReadLineAsync(stoppingToken))
				{
					switch (state)
					{
						case State.Starting:
							state = Starting(line);
							break;
						case State.WaitingForIdent:
							state = WaitingForIdent(line);
							break;
						case State.WaitingForData:
							state = WaitingForData(line);
							break;
						case State.Data:
							state = await Data(line, stoppingToken);
							break;
					}
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "Error reading from Dsmr");
			}
		}
	}

	private State Starting(string line)
	{
		if (GetIdentRegex().IsMatch(line))
		{
			return WaitingForIdent(line);
		}
		_logger.LogInformation("{Line} dropped, waiting for ident line", line);
		return State.Starting;
	}

	private State WaitingForIdent(string line)
	{
		if (GetIdentRegex().IsMatch(line))
		{
			_values.Clear();
			_crc.Reset();
			_crc.UpdateWithLine(line);
			return State.WaitingForData;
		}
		_logger.LogError("{Line} dropped", line);
		return State.WaitingForIdent;
	}

	private State WaitingForData(string line)
	{
		if (line == "")
		{
			_crc.UpdateWithLine(line);
			return State.Data;
		}
		_logger.LogError("{Line} dropped", line);
		return State.WaitingForIdent;
	}

	private async ValueTask<State> Data(string line, CancellationToken stoppingToken)
	{
		Match dataMatch = GetDataLineRegex().Match(line);
		if (dataMatch.Success)
		{
			return ProcessData(line, dataMatch);
		}

		Match crcMatch = GetCrcLineRegex().Match(line);
		if (crcMatch.Success)
		{
			return await ProcessCrc(line, crcMatch, stoppingToken);
		}

		_logger.LogError("{Line} dropped", line);
		return State.WaitingForIdent;
	}

	private State ProcessData(string line, Match dataMatch)
	{
		_crc.UpdateWithLine(line);
		string id = dataMatch.Groups["id"].Value;
		if (ObisMappingsById.TryGetValue(id, out ObisMapping? mapping) && mapping != null)
		{
			P1Value value = mapping.CreateValue(dataMatch.Groups["data"].Value);
			if (value.IsValid)
			{
				_values.Add(value);
				_logger.LogDebug("{Value} parsed", value);
			}
			else
			{
				_logger.LogError("{Line} parsing of value failed", line);
			}
		}
		else
		{
			_logger.LogWarning("{Line} unknown obis id, line dropped", line);
		}
		return State.Data;
	}

	private async ValueTask<State> ProcessCrc(string line, Match crcMatch, CancellationToken stoppingToken)
	{
		_crc.Update('!');
		if (crcMatch.Groups["crc"].Value == _crc.GetCrc())
		{
			await _valuesWriter.WriteAsync(_values.ToList(), stoppingToken);
			_logger.LogDebug("Values enqueued");
		}
		else
		{
			_logger.LogError("{Line} crc is invalid", line);
		}
		return State.WaitingForIdent;
	}

	[GeneratedRegex(@"^/...5\d+\z")]
	private static partial Regex GetIdentRegex();

	[GeneratedRegex(@"^(?<id>[01]-0:\d+.\d+.\d+)(?<datalist>\((?<data>.+)\))+\z")]
	private static partial Regex GetDataLineRegex();

	[GeneratedRegex(@"^!(?<crc>[0-9A-F]{4})\z")]
	private static partial Regex GetCrcLineRegex();
}
