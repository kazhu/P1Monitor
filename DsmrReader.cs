using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace P1Monitor;

public partial class DsmrReader : BackgroundService
{
	private readonly ILogger<DsmrReader> _logger;
	private readonly ConcurrentQueue<List<P1Value>> _valuesQueue;
	private readonly DsmrReaderOptions _options;
	private readonly List<P1Value> _values = new();
	private readonly ModbusCrc _crc = new();

	private enum State
	{
		Starting,
		WaitingForIdent,
		WaitingForData,
		Data,
	}

	public DsmrReader(ILogger<DsmrReader> logger, ConcurrentQueue<List<P1Value>> valuesQueue, IOptions<DsmrReaderOptions> options)
	{
		_logger = logger;
		_valuesQueue = valuesQueue;
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
							state = Data(line);
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
			_crc.Reset();
			_values.Clear();
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

	private State Data(string line)
	{
		Match dataMatch = GetDataLineRegex().Match(line);
		if (dataMatch.Success)
		{
			_crc.UpdateWithLine(line);
			P1Value? value = GetValue(dataMatch);
			if (value == null)
			{
				_logger.LogWarning("{Line} unknown id, line dropped", line);
			}
			else
			{
				if (!value.IsValid)
				{
					_logger.LogError("{Line} parsing of value failed", line);
				}
				else
				{
					_values.Add(value);
					_logger.LogDebug("{Value} parsed", value);
				}
			}
			return State.Data;
		}

		Match crcMatch = GetCrcLineRegex().Match(line);
		if (crcMatch.Success)
		{
			_crc.Update('!');
			if (crcMatch.Groups["crc"].Value == _crc.GetCrc())
			{
				_valuesQueue.Enqueue(_values.ToList());
				_logger.LogDebug("Values enqueued");
			}
			else
			{
				_logger.LogError("{Line} crc is invalid", line);
			}
			return State.WaitingForIdent;
		}

		_logger.LogError("{Line} dropped", line);
		return State.WaitingForIdent;
	}

	private static P1Value? GetValue(Match match)
	{
		string id = match.Groups["id"].Value;
		string data = match.Groups["data"].Value;
		switch (id)
		{
			case "0-0:1.0.0": return new P1TimeValue(id, data, "time");
			case "0-0:42.0.0": return new P1StringValue(id, data, "name");
			case "0-0:96.1.0": return new P1StringValue(id, data, "serial");
			case "0-0:96.14.0": return new P1Number4Value(id, data, "tariff");
			case "0-0:96.50.68": return new P1OnOffValue(id, data, "state");
			case "1-0:1.8.0": return new P1kWhValue(id, data, "import_energy");
			case "1-0:1.8.1": return new P1kWhValue(id, data, "import_energy_tariff_1");
			case "1-0:1.8.2": return new P1kWhValue(id, data, "import_energy_tariff_2");
			case "1-0:1.8.3": return new P1kWhValue(id, data, "import_energy_tariff_3");
			case "1-0:1.8.4": return new P1kWhValue(id, data, "import_energy_tariff_4");
			case "1-0:2.8.0": return new P1kWhValue(id, data, "export_energy");
			case "1-0:2.8.1": return new P1kWhValue(id, data, "export_energy_tariff_1");
			case "1-0:2.8.2": return new P1kWhValue(id, data, "export_energy_tariff_2");
			case "1-0:2.8.3": return new P1kWhValue(id, data, "export_energy_tariff_3");
			case "1-0:2.8.4": return new P1kWhValue(id, data, "export_energy_tariff_4");
			case "1-0:3.8.0": return new P1kvarhValue(id, data, "import_reactive_energy");
			case "1-0:4.8.0": return new P1kvarhValue(id, data, "export_reactive_energy");
			case "1-0:5.8.0": return new P1kvarhValue(id, data, "reactive_energy_q1");
			case "1-0:6.8.0": return new P1kvarhValue(id, data, "reactive_energy_q2");
			case "1-0:7.8.0": return new P1kvarhValue(id, data, "reactive_energy_q3");
			case "1-0:8.8.0": return new P1kvarhValue(id, data, "reactive_energy_q4");
			case "1-0:32.7.0": return new P1VoltValue(id, data, "voltage_l1");
			case "1-0:52.7.0": return new P1VoltValue(id, data, "voltage_l2");
			case "1-0:72.7.0": return new P1VoltValue(id, data, "voltage_l3");
			case "1-0:31.7.0": return new P1AmpereValue(id, data, "current_l1");
			case "1-0:51.7.0": return new P1AmpereValue(id, data, "current_l2");
			case "1-0:71.7.0": return new P1AmpereValue(id, data, "current_l3");
			case "1-0:13.7.0": return new P1PowerFactorValue(id, data, "power_factor");
			case "1-0:33.7.0": return new P1PowerFactorValue(id, data, "power_factor_l1");
			case "1-0:53.7.0": return new P1PowerFactorValue(id, data, "power_factor_l2");
			case "1-0:73.7.0": return new P1PowerFactorValue(id, data, "power_factor_l3");
			case "1-0:14.7.0": return new P1HzValue(id, data, "frequency");
			case "1-0:1.7.0": return new P1kWValue(id, data, "import_power");
			case "1-0:2.7.0": return new P1kWValue(id, data, "export_power");
			case "1-0:5.7.0": return new P1kvarValue(id, data, "reactive_power_q1");
			case "1-0:6.7.0": return new P1kvarValue(id, data, "reactive_power_q2");
			case "1-0:7.7.0": return new P1kvarValue(id, data, "reactive_power_q3");
			case "1-0:8.7.0": return new P1kvarValue(id, data, "reactive_power_q4");
			case "0-0:17.0.0": // Limiter határérték
			case "1-0:15.8.0": // Hatásos energia kombinált
			case "1-0:31.4.0": // Áram korlátozás határérték 1
			case "1-0:51.4.0": // Áram korlátozás határérték 2
			case "1-0:71.4.0": // Áram korlátozás határérték 3
			case "0-0:98.1.0": // Hónap végi tárolt adatok
			case "0-0:96.13.0": // Áramszolgáltatói szöveges üzenet
				return new P1NoValue(id, data);
			default:
				return null;
		}
	}

	[GeneratedRegex(@"^/...5\d+\z")]
	private static partial Regex GetIdentRegex();

	[GeneratedRegex(@"^(?<id>[01]-0:\d+.\d+.\d+)(?<datalist>\((?<data>.+)\))+\z")]
	private static partial Regex GetDataLineRegex();

	[GeneratedRegex(@"^!(?<crc>[0-9A-F]{4})\z")]
	private static partial Regex GetCrcLineRegex();
}
