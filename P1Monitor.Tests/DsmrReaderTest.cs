using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace P1Monitor.Tests;

[TestClass]
public class DsmrReaderTest
{
	private readonly TestLogger<DsmrReader> _logger = new();
	private readonly TestInfluxDbWriter _influxDbWriter = new();
	private readonly IOptions<DsmrReaderOptions> _options = Options.Create(new DsmrReaderOptions { Host = "localhost", Port = 2323 });
	private readonly DsmrParser _parser;
	private readonly DsmrReader _reader;

	public DsmrReaderTest()
	{
		_parser = new DsmrParser(NullLogger<DsmrParser>.Instance);
		_reader = new DsmrReader(_logger, _influxDbWriter, _parser, _options);
	}

	private const string SampleDatagram =
		"""
		/AUX59903218166

		0-0:1.0.0(230817171430S)
		0-0:42.0.0(AUX1030303218166)
		0-0:96.1.0(9903218166)
		0-0:96.14.0(0001)
		0-0:96.50.68(ON)
		0-0:17.0.0(90.000*kW)
		1-0:1.8.0(000812.421*kWh)
		1-0:1.8.1(000470.111*kWh)
		1-0:1.8.2(000342.310*kWh)
		1-0:1.8.3(000000.000*kWh)
		1-0:1.8.4(000000.000*kWh)
		1-0:2.8.0(001714.369*kWh)
		1-0:2.8.1(001233.413*kWh)
		1-0:2.8.2(000480.956*kWh)
		1-0:2.8.3(000000.000*kWh)
		1-0:2.8.4(000000.000*kWh)
		1-0:3.8.0(000018.858*kvarh)
		1-0:4.8.0(000439.269*kvarh)
		1-0:5.8.0(000011.481*kvarh)
		1-0:6.8.0(000007.377*kvarh)
		1-0:7.8.0(000186.705*kvarh)
		1-0:8.8.0(000252.564*kvarh)
		1-0:15.8.0(002526.790*kWh)
		1-0:32.7.0(234.0*V)
		1-0:52.7.0(232.7*V)
		1-0:72.7.0(233.5*V)
		1-0:31.7.0(001*A)
		1-0:51.7.0(000*A)
		1-0:71.7.0(000*A)
		1-0:13.7.0(0.336)
		1-0:33.7.0(0.842)
		1-0:53.7.0(0.989)
		1-0:73.7.0(0.845)
		1-0:14.7.0(49.99*Hz)
		1-0:1.7.0(00.250*kW)
		1-0:2.7.0(00.168*kW)
		1-0:5.7.0(00.000*kvar)
		1-0:6.7.0(00.000*kvar)
		1-0:7.7.0(00.066*kvar)
		1-0:8.7.0(00.159*kvar)
		1-0:31.4.0(200*A)
		1-0:51.4.0(200*A)
		1-0:71.4.0(200*A)
		0-0:98.1.0(230801000000S)(000663.924*kWh)(000383.623*kWh)(000280.301*kWh)(001304.175*kWh)(000906.937*kWh)(000397.238*kWh)(000015.142*kvarh)(000333.817*kvarh)(000009.479*kvarh)(000005.663*kvarh)(000142.881*kvarh)(000190.936*kvarh)(001968.098*kWh)(12.212*kW)(12.212*kW)(11.320*kW)(05.220*kW)(05.220*kW)(05.144*kW)
		0-0:96.13.0(ÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿ)
		!5C2E

		""";

	[TestMethod]
	public void TestAllHappy()
	{
		byte[] datagram = Encoding.Latin1.GetBytes(SampleDatagram);

		int result = _reader.ProcessBuffer(datagram);

		Assert.AreEqual(0, result);
		Assert.AreEqual(1, _influxDbWriter.Values.Count);

		var values = _influxDbWriter.Values[0];
		Assert.AreEqual(ObisMapping.Mappings.Length, values.Length);
		for (int i = 0; i < ObisMapping.Mappings.Length; i++)
		{
			var mapping = ObisMapping.Mappings[i];
			string data = Encoding.Latin1.GetString(values[mapping.Index].Data.Memory.Span);
			if (mapping.P1Type == P1Type.Number && mapping.Unit != P1Unit.None)
			{
				data += "/" + mapping.Unit;
			}
			var expectedValue = mapping.FieldName switch
			{
				"time" => "230817171430S",
				"name" => "AUX1030303218166",
				"serial" => "9903218166",
				"tariff" => "1",
				"state" => "ON",
				"limiter_limit" => "90.000*kW",
				"import_energy" => "812.421/kWh",
				"import_energy_tariff_1" => "470.111/kWh",
				"import_energy_tariff_2" => "342.31/kWh",
				"import_energy_tariff_3" => "0.0/kWh",
				"import_energy_tariff_4" => "0.0/kWh",
				"export_energy" => "1714.369/kWh",
				"export_energy_tariff_1" => "1233.413/kWh",
				"export_energy_tariff_2" => "480.956/kWh",
				"export_energy_tariff_3" => "0.0/kWh",
				"export_energy_tariff_4" => "0.0/kWh",
				"import_reactive_energy" => "18.858/kvarh",
				"export_reactive_energy" => "439.269/kvarh",
				"reactive_energy_q1" => "11.481/kvarh",
				"reactive_energy_q2" => "7.377/kvarh",
				"reactive_energy_q3" => "186.705/kvarh",
				"reactive_energy_q4" => "252.564/kvarh",
				"energy_combined" => "002526.790*kWh",
				"voltage_l1" => "234.0/V",
				"voltage_l2" => "232.7/V",
				"voltage_l3" => "233.5/V",
				"current_l1" => "1/A",
				"current_l2" => "0/A",
				"current_l3" => "0/A",
				"power_factor" => "0.336",
				"power_factor_l1" => "0.842",
				"power_factor_l2" => "0.989",
				"power_factor_l3" => "0.845",
				"frequency" => "49.99/Hz",
				"import_power" => "0.25/kW",
				"export_power" => "0.168/kW",
				"reactive_power_q1" => "0.0/kvar",
				"reactive_power_q2" => "0.0/kvar",
				"reactive_power_q3" => "0.066/kvar",
				"reactive_power_q4" => "0.159/kvar",
				"current_limit_l1" => "200*A",
				"current_limit_l2" => "200*A",
				"current_limit_l3" => "200*A",
				"previous_month" => "230801000000S)(000663.924*kWh)(000383.623*kWh)(000280.301*kWh)(001304.175*kWh)(000906.937*kWh)(000397.238*kWh)(000015.142*kvarh)(000333.817*kvarh)(000009.479*kvarh)(000005.663*kvarh)(000142.881*kvarh)(000190.936*kvarh)(001968.098*kWh)(12.212*kW)(12.212*kW)(11.320*kW)(05.220*kW)(05.220*kW)(05.144*kW",
				"message" => new string('ÿ', 1024),
				_ => "",
			};
			if (expectedValue != "")
			{
				Assert.AreEqual(expectedValue, data, $"Field {Encoding.Latin1.GetString(mapping.Id.Memory.Span)}/{mapping.FieldName} is different");
			}
		}

		List<string> debugMessages = _logger.Messages.ContainsKey(LogLevel.Debug) ? _logger.Messages[LogLevel.Debug] : new();
		_logger.Messages.Remove(LogLevel.Debug);
		_logger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_logger.Messages));
		Assert.AreEqual(1, debugMessages.Count);
		Assert.IsTrue(debugMessages[0].Contains("Enqueuing values for "));
	}

	[TestMethod]
	public void TestTwoDatagrams()
	{
		byte[] datagram = Encoding.Latin1.GetBytes(SampleDatagram + SampleDatagram);

		int result = _reader.ProcessBuffer(datagram);

		Assert.AreEqual(0, result);
		Assert.AreEqual(2, _influxDbWriter.Values.Count);
		_logger.Messages.Remove(LogLevel.Trace);
		_logger.Messages.Remove(LogLevel.Debug);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_logger.Messages));
	}

	[TestMethod]
	public void TestMissingData()
	{
		const string SampleDatagramWithMissingLine =
			"""
			/AUX59903218166

			0-0:1.0.0(230817171430S)
			0-0:42.0.0(AUX1030303218166)
			0-0:96.1.0(9903218166)
			0-0:96.14.0(0001)
			0-0:96.50.68(ON)
			0-0:17.0.0(90.000*kW)
			1-0:1.8.0(000812.421*kWh)
			1-0:1.8.1(000470.111*kWh)
			1-0:1.8.2(000342.310*kWh)
			1-0:1.8.3(000000.000*kWh)
			1-0:1.8.4(000000.000*kWh)
			1-0:2.8.0(001714.369*kWh)
			1-0:2.8.1(001233.413*kWh)
			1-0:2.8.2(000480.956*kWh)
			1-0:2.8.3(000000.000*kWh)
			1-0:2.8.4(000000.000*kWh)
			1-0:3.8.0(000018.858*kvarh)
			1-0:4.8.0(000439.269*kvarh)
			1-0:5.8.0(000011.481*kvarh)
			1-0:6.8.0(000007.377*kvarh)
			1-0:7.8.0(000186.705*kvarh)
			1-0:8.8.0(000252.564*kvarh)
			1-0:15.8.0(002526.790*kWh)
			1-0:52.7.0(232.7*V)
			1-0:72.7.0(233.5*V)
			1-0:31.7.0(001*A)
			1-0:51.7.0(000*A)
			1-0:71.7.0(000*A)
			1-0:13.7.0(0.336)
			1-0:33.7.0(0.842)
			1-0:53.7.0(0.989)
			1-0:73.7.0(0.845)
			1-0:14.7.0(49.99*Hz)
			1-0:1.7.0(00.250*kW)
			1-0:2.7.0(00.168*kW)
			1-0:5.7.0(00.000*kvar)
			1-0:6.7.0(00.000*kvar)
			1-0:7.7.0(00.066*kvar)
			1-0:8.7.0(00.159*kvar)
			1-0:31.4.0(200*A)
			1-0:51.4.0(200*A)
			1-0:71.4.0(200*A)
			0-0:98.1.0(230801000000S)(000663.924*kWh)(000383.623*kWh)(000280.301*kWh)(001304.175*kWh)(000906.937*kWh)(000397.238*kWh)(000015.142*kvarh)(000333.817*kvarh)(000009.479*kvarh)(000005.663*kvarh)(000142.881*kvarh)(000190.936*kvarh)(001968.098*kWh)(12.212*kW)(12.212*kW)(11.320*kW)(05.220*kW)(05.220*kW)(05.144*kW)
			0-0:96.13.0(ÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿ)
			!7133

			""";
		byte[] datagram = Encoding.Latin1.GetBytes(SampleDatagramWithMissingLine);

		int result = _reader.ProcessBuffer(datagram);

		Assert.AreEqual(0, result);
		Assert.AreEqual(0, _influxDbWriter.Values.Count);
		_logger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{\"Error\":[\"1-0:32.7.0 is missing, dropping all values\"]}", JsonSerializer.Serialize(_logger.Messages));
	}

	[TestMethod]
	public void TestDuplicatedValue()
	{
		const string SampleDatagramWithDuplicatedValue =
			"""
			/AUX59903218166
			
			0-0:1.0.0(230817171430S)
			0-0:1.0.0(230817171430S)
			0-0:42.0.0(AUX1030303218166)
			0-0:96.1.0(9903218166)
			0-0:96.14.0(0001)
			0-0:96.50.68(ON)
			0-0:17.0.0(90.000*kW)
			1-0:1.8.0(000812.421*kWh)
			1-0:1.8.1(000470.111*kWh)
			1-0:1.8.2(000342.310*kWh)
			1-0:1.8.3(000000.000*kWh)
			1-0:1.8.4(000000.000*kWh)
			1-0:2.8.0(001714.369*kWh)
			1-0:2.8.1(001233.413*kWh)
			1-0:2.8.2(000480.956*kWh)
			1-0:2.8.3(000000.000*kWh)
			1-0:2.8.4(000000.000*kWh)
			1-0:3.8.0(000018.858*kvarh)
			1-0:4.8.0(000439.269*kvarh)
			1-0:5.8.0(000011.481*kvarh)
			1-0:6.8.0(000007.377*kvarh)
			1-0:7.8.0(000186.705*kvarh)
			1-0:8.8.0(000252.564*kvarh)
			1-0:15.8.0(002526.790*kWh)
			1-0:32.7.0(234.0*V)
			1-0:52.7.0(232.7*V)
			1-0:72.7.0(233.5*V)
			1-0:31.7.0(001*A)
			1-0:51.7.0(000*A)
			1-0:71.7.0(000*A)
			1-0:13.7.0(0.336)
			1-0:33.7.0(0.842)
			1-0:53.7.0(0.989)
			1-0:73.7.0(0.845)
			1-0:14.7.0(49.99*Hz)
			1-0:1.7.0(00.250*kW)
			1-0:2.7.0(00.168*kW)
			1-0:5.7.0(00.000*kvar)
			1-0:6.7.0(00.000*kvar)
			1-0:7.7.0(00.066*kvar)
			1-0:8.7.0(00.159*kvar)
			1-0:31.4.0(200*A)
			1-0:51.4.0(200*A)
			1-0:71.4.0(200*A)
			0-0:98.1.0(230801000000S)(000663.924*kWh)(000383.623*kWh)(000280.301*kWh)(001304.175*kWh)(000906.937*kWh)(000397.238*kWh)(000015.142*kvarh)(000333.817*kvarh)(000009.479*kvarh)(000005.663*kvarh)(000142.881*kvarh)(000190.936*kvarh)(001968.098*kWh)(12.212*kW)(12.212*kW)(11.320*kW)(05.220*kW)(05.220*kW)(05.144*kW)
			0-0:96.13.0(ÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿÿ)
			!FD77
			
			""";
		byte[] datagram = Encoding.Latin1.GetBytes(SampleDatagramWithDuplicatedValue);

		int result = _reader.ProcessBuffer(datagram);

		Assert.AreEqual(0, result);
		Assert.AreEqual(0, _influxDbWriter.Values.Count);
		_logger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{\"Error\":[\"0-0:1.0.0 - time: Time 230817171430S (None): duplicated value\"]}", JsonSerializer.Serialize(_logger.Messages));
	}
}
