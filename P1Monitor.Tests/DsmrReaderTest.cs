using Microsoft.Extensions.Logging;
using P1Monitor.Options;
using System.Text;
using System.Text.Json;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace P1Monitor.Tests;

[TestClass]
public class DsmrReaderTest
{
	private readonly TestLogger<DsmrReader> _readerLogger = new();
	private readonly TestLogger<DsmrParser> _parserLogger = new();
	private readonly TestInfluxDbWriter _influxDbWriter = new();
	private readonly DsmrReaderOptions _options = new() { Host = "localhost", Port = 2323 };
	private readonly IObisMappingsProvider _obisMappingProvider = new TestObisMappingsProvider();
	private readonly DsmrParser _parser;
	private readonly DsmrReader _reader;

	public DsmrReaderTest()
	{
		_parser = new DsmrParser(_parserLogger, _obisMappingProvider);
		_reader = new DsmrReader(_readerLogger, _influxDbWriter, _parser, _obisMappingProvider, OptionsFactory.Create(_options));
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
		DsmrValue[] expectedValues = _obisMappingProvider.Mappings
			.OrderBy(x => x.Index)
			.Select(m => (DsmrValue) (m.FieldName switch
			{
				"time" => new DsmrTimeValue(m, DateTimeOffset.ParseExact("2023-08-17T17:14:30.0000000+02:00", "O", null)),
				"name" => new DsmrStringValue(m, "AUX1030303218166"),
				"serial" => new DsmrStringValue(m, "9903218166"),
				"tariff" => new DsmrNumberValue(m, 1m),
				"state" => new DsmrOnOffValue(m, DsmrOnOffValue.OnOff.ON),
				"limiter_limit" => new DsmrIgnoredValue(m),
				"import_energy" => new DsmrNumberValue(m, 812.421m),
				"import_energy_tariff_1" => new DsmrNumberValue(m, 470.111m),
				"import_energy_tariff_2" => new DsmrNumberValue(m, 342.31m),
				"import_energy_tariff_3" => new DsmrNumberValue(m, 0m),
				"import_energy_tariff_4" => new DsmrNumberValue(m, 0m),
				"export_energy" => new DsmrNumberValue(m, 1714.369m),
				"export_energy_tariff_1" => new DsmrNumberValue(m, 1233.413m),
				"export_energy_tariff_2" => new DsmrNumberValue(m, 480.956m),
				"export_energy_tariff_3" => new DsmrNumberValue(m, 0m),
				"export_energy_tariff_4" => new DsmrNumberValue(m, 0m),
				"import_reactive_energy" => new DsmrNumberValue(m, 18.858m),
				"export_reactive_energy" => new DsmrNumberValue(m, 439.269m),
				"reactive_energy_q1" => new DsmrNumberValue(m, 11.481m),
				"reactive_energy_q2" => new DsmrNumberValue(m, 7.377m),
				"reactive_energy_q3" => new DsmrNumberValue(m, 186.705m),
				"reactive_energy_q4" => new DsmrNumberValue(m, 252.564m),
				"energy_combined" => new DsmrIgnoredValue(m),
				"voltage_l1" => new DsmrNumberValue(m, 234m),
				"voltage_l2" => new DsmrNumberValue(m, 232.7m),
				"voltage_l3" => new DsmrNumberValue(m, 233.5m),
				"current_l1" => new DsmrNumberValue(m, 1m),
				"current_l2" => new DsmrNumberValue(m, 0m),
				"current_l3" => new DsmrNumberValue(m, 0m),
				"power_factor" => new DsmrNumberValue(m, 0.336m),
				"power_factor_l1" => new DsmrNumberValue(m, 0.842m),
				"power_factor_l2" => new DsmrNumberValue(m, 0.989m),
				"power_factor_l3" => new DsmrNumberValue(m, 0.845m),
				"frequency" => new DsmrNumberValue(m, 49.99m),
				"import_power" => new DsmrNumberValue(m, 0.25m),
				"export_power" => new DsmrNumberValue(m, 0.168m),
				"reactive_power_q1" => new DsmrNumberValue(m, 0m),
				"reactive_power_q2" => new DsmrNumberValue(m, 0m),
				"reactive_power_q3" => new DsmrNumberValue(m, 0.066m),
				"reactive_power_q4" => new DsmrNumberValue(m, 0.159m),
				"current_limit_l1" => new DsmrIgnoredValue(m),
				"current_limit_l2" => new DsmrIgnoredValue(m),
				"current_limit_l3" => new DsmrIgnoredValue(m),
				"previous_month" => new DsmrIgnoredValue(m),
				"message" => new DsmrIgnoredValue(m),
				_ => throw new Exception($"Unexpected field name {m.FieldName}"),
			}))
			.ToArray();

		int result = _reader.ProcessBuffer(datagram);

		Assert.AreEqual(0, result);
		Assert.AreEqual(1, _influxDbWriter.Values.Count);

		DsmrValue[] values = _influxDbWriter.Values[0];

		Assert.AreEqual(expectedValues.Length, values.Length);
		for (int i = 0; i < values.Length; i++)
		{
			Assert.AreEqual(expectedValues[i], values[i], $"Comparing {expectedValues[i].Mapping.FieldName}");
		}

		_parserLogger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_parserLogger.Messages));
		Assert.AreEqual("{\"Debug\":[\"Enqueuing values for 08/17/2023 17:14:30 \\u002B02:00\"]}", JsonSerializer.Serialize(_readerLogger.Messages));
	}

	[TestMethod]
	public void TestTwoDatagrams()
	{
		byte[] datagram = Encoding.Latin1.GetBytes(SampleDatagram + SampleDatagram);

		int result = _reader.ProcessBuffer(datagram);

		Assert.AreEqual(0, result);
		Assert.AreEqual(2, _influxDbWriter.Values.Count);
		_parserLogger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_parserLogger.Messages));
		_readerLogger.Messages.Remove(LogLevel.Debug);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_readerLogger.Messages));
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
		_parserLogger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_parserLogger.Messages));
		Assert.AreEqual("{\"Error\":[\"1-0:32.7.0 is missing, dropping all values\"]}", JsonSerializer.Serialize(_readerLogger.Messages));
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
		Assert.AreEqual(1, _influxDbWriter.Values.Count);
		_parserLogger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{\"Error\":[\"0-0:1.0.0(230817171430S): duplicated value\"]}", JsonSerializer.Serialize(_parserLogger.Messages));
		_readerLogger.Messages.Remove(LogLevel.Debug);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_readerLogger.Messages));
	}
}
