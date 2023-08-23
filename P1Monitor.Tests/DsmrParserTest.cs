using System.Text;
using System.Text.Json;
using P1Monitor.Model;

namespace P1Monitor.Tests;

[TestClass]
public class DsmrParserTest
{
	private readonly TestLogger<DsmrParser> _logger;
	private readonly IObisMappingsProvider _obisMappingProvider = new TestObisMappingsProvider();
	private readonly DsmrParser _parser;

	public DsmrParserTest()
	{
		_logger = new TestLogger<DsmrParser>();
		_parser = new DsmrParser(_logger, _obisMappingProvider);
	}

	[TestMethod]
	public void TestTryFindDataLinesSecondDatagramGarbageError()
	{
		ReadOnlySpan<byte> buffer = Encoding.Latin1.GetBytes("/abc512\r\n\r\n\r\n!774B\r\ngarbage\r\n/abc512\r\n\r\n\r\n!774B\r\n");

		bool result = _parser.TryFindDataLines(ref buffer, out ReadOnlySpan<byte> dataLine);
		Assert.IsTrue(result);
		Assert.AreEqual("", Encoding.Latin1.GetString(dataLine));
		Assert.AreEqual("garbage\r\n/abc512\r\n\r\n\r\n!774B\r\n", Encoding.Latin1.GetString(buffer));
		Assert.AreEqual("{}", JsonSerializer.Serialize(_logger.Messages));

		result = _parser.TryFindDataLines(ref buffer, out ReadOnlySpan<byte> dataLine2);
		Assert.IsTrue(result);
		Assert.AreEqual("", Encoding.Latin1.GetString(dataLine2));
		Assert.AreEqual("", Encoding.Latin1.GetString(buffer));

		Assert.AreEqual("{\"Error\":[\"Dropped data before datagram start: garbage\\r\\n\"]}", JsonSerializer.Serialize(_logger.Messages));
	}

	[DataTestMethod]
	[DataRow("/abc512\r\n\r\n\r\n!774B\r\n", "", "")]
	[DataRow("garbage\r\n/abc512\r\n\r\n\r\n!774B\r\n", "", "", "{\"Information\":[\"Dropped data before datagram start: garbage\\r\\n\"]}")]
	[DataRow("/abc512\r\n\r\ndataline1\r\ndataline2\r\n!12EC\r\n", "dataline1\r\ndataline2", "")]
	[DataRow("/abc512\r\n\r\ndataline1\r\ndataline2\r\n!12EC\r\n/rem5ining\r\n\r\n\r\n!0000\r\n", "dataline1\r\ndataline2", "/rem5ining\r\n\r\n\r\n!0000\r\n")]
	public void TestTryFindDataLinesHappy(string input, string expectedDataLine, string remaining, string expectedLog = "{}")
	{
		ReadOnlySpan<byte> buffer = Encoding.Latin1.GetBytes(input);

		bool result = _parser.TryFindDataLines(ref buffer, out ReadOnlySpan<byte> dataLine);

		Assert.IsTrue(result);
		Assert.AreEqual(expectedDataLine, Encoding.Latin1.GetString(dataLine));
		Assert.AreEqual(remaining, Encoding.Latin1.GetString(buffer));
		Assert.AreEqual(expectedLog, JsonSerializer.Serialize(_logger.Messages));
	}


	[DataTestMethod]
	[DataRow("")]
	[DataRow("garbage")]
	[DataRow("garbage\r\n")]
	[DataRow("/abc512")]
	[DataRow("/abc512\r\n")]
	[DataRow("/abc512\r\n\r\ndataline1\r\ndataline2\r\n")]
	[DataRow("/abc512\r\n\r\ndataline1\r\ndataline2\r\n!12EC")]
	public void TestTryFindDataLinesPartial(string input)
	{
		ReadOnlySpan<byte> buffer = Encoding.Latin1.GetBytes(input);

		bool result = _parser.TryFindDataLines(ref buffer, out ReadOnlySpan<byte> dataLine);

		Assert.IsFalse(result);
		Assert.AreEqual("", Encoding.Latin1.GetString(dataLine));
		Assert.AreEqual(input, Encoding.Latin1.GetString(buffer));
		Assert.AreEqual("{}", JsonSerializer.Serialize(_logger.Messages));
	}

	[DataTestMethod]
	[DataRow("garbage\r\n/", "/", "{\"Information\":[\"Dropped data before datagram start: garbage\\r\\n\"]}")]
	[DataRow("/abcx12\r\n\r\n", "\r\n", "{\"Error\":[\"Invalid identification line, dropped the line /abcx12\"]}")]
	[DataRow("/abc512\r\nerror", "error", "{\"Error\":[\"Invalid identification line, dropped the line /abc512\"]}")]
	[DataRow("/abc512\r\n\ra", "\ra", "{\"Error\":[\"Invalid identification line, dropped the line /abc512\"]}")]
	[DataRow("/abc512\r\n\r\ndataline1\r\ndataline2\r\n!0000\r\n", "", "{\"Error\":[\"Invalid CRC, dropped the datagram /abc512\\r\\n\\r\\ndataline1\\r\\ndataline2\\r\\n!0000\\r\\n\"]}")]
	public void TestTryFindDataLinesFailures(string input, string remaining, string expectedLog)
	{
		ReadOnlySpan<byte> buffer = Encoding.Latin1.GetBytes(input);

		bool result = _parser.TryFindDataLines(ref buffer, out ReadOnlySpan<byte> dataLine);

		Assert.IsFalse(result);
		Assert.AreEqual("", Encoding.Latin1.GetString(dataLine));
		Assert.AreEqual(remaining, Encoding.Latin1.GetString(buffer));
		Assert.AreEqual(expectedLog, JsonSerializer.Serialize(_logger.Messages));
	}

	[DataTestMethod]
	[DataRow("0-0:1.0.0(230817171430S)", "230817171430S", "time", "2023-08-17T17:14:30.0000000")]
	[DataRow("0-0:1.0.0(230817171430S)\r\n", "230817171430S", "time", "2023-08-17T17:14:30.0000000")]
	[DataRow("0-0:1.0.0(230817171430S)\r\nremaining", "230817171430S", "time", "2023-08-17T17:14:30.0000000", "remaining")]
	[DataRow("0-0:42.0.0(AUX1030303218166)", "AUX1030303218166", "name")]
	[DataRow("0-0:96.50.68(ON)", "ON", "state")]
	[DataRow("0-0:96.14.0(0001)", "1", "tariff")]
	[DataRow("0-0:96.14.0(0000)", "0", "tariff")]
	[DataRow("1-0:1.8.0(000812.421*kWh)", "812.421", "import_energy")]
	[DataRow("1-0:3.8.0(000018.858*kvarh)", "18.858", "import_reactive_energy")]
	[DataRow("1-0:32.7.0(234.0*V)", "234.0", "voltage_l1")]
	[DataRow("1-0:31.7.0(001*A)", "1", "current_l1")]
	[DataRow("1-0:13.7.0(0.336)", "0.336", "power_factor")]
	[DataRow("1-0:14.7.0(49.99*Hz)", "49.99", "frequency")]
	[DataRow("1-0:1.7.0(00.250*kW)", "0.25", "import_power")]
	[DataRow("1-0:5.7.0(00.000*kvar)", "0.0", "reactive_power_q1")]
	[DataRow("0-0:98.1.0(230801000000S)(000663.924*kWh)", "230801000000S)(000663.924*kWh", "previous_month")]
	public void TestTryParseDataLine(string input, string expectedValue, string fieldName, string? expectedTime = null, string remaining = "")
	{
		var inputBytes = new ReadOnlySpan<byte>(Encoding.Latin1.GetBytes(input));

		bool result = _parser.TryParseDataLine(ref inputBytes, out P1Value p1Value);

		Assert.IsTrue(result);
		Assert.AreEqual(remaining, Encoding.Latin1.GetString(inputBytes));
		using (p1Value)
		{
			Assert.IsFalse(p1Value.IsEmpty);
			Assert.IsNotNull(p1Value.Mapping);
			Assert.AreEqual(fieldName, p1Value.Mapping.FieldName);
			Assert.IsTrue(p1Value.IsValid);
			Assert.AreEqual(expectedValue, Encoding.Latin1.GetString(p1Value.Data.Memory.Span));
			Assert.AreEqual(expectedTime == null ? null : new DateTimeOffset(DateTime.ParseExact(expectedTime, "O", null)), p1Value.Time);
		}
	}

	[DataTestMethod]
	[DataRow("0-0:96.50.68ON)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"0-0:96.50.68ON): not well formed, dropped\"]}")]
	[DataRow("0-0:96.50.68ON)", "", "{\"Error\":[\"0-0:96.50.68ON): not well formed, dropped\"]}")]
	[DataRow("(ON)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"(ON): not well formed, dropped\"]}")]
	[DataRow("0-0:0.0.0(ON)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Warning\":[\"0-0:0.0.0(ON): unknown obis id, line dropped\"]}")]
	[DataRow("0-0:42.0.0(123456789012345678901234567890123)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"0-0:42.0.0(123456789012345678901234567890123): parsing of value failed\"]}")]
	[DataRow("1-0:13.7.0(.336)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"1-0:13.7.0(.336): parsing of value failed\"]}")]
	[DataRow("0-0:1.0.0(230817171430)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"0-0:1.0.0(230817171430): parsing of value failed\"]}")]
	[DataRow("0-0:96.50.68(on)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"0-0:96.50.68(on): parsing of value failed\"]}")]
	public void TestTryParseDataLineFailure(string input, string remaining, string expectedLog)
	{
		var inputBytes = new ReadOnlySpan<byte>(Encoding.Latin1.GetBytes(input));

		Assert.IsFalse(_parser.TryParseDataLine(ref inputBytes, out P1Value p1Value));

		Assert.AreEqual(remaining, Encoding.Latin1.GetString(inputBytes));
		Assert.IsTrue(p1Value.IsEmpty);
		Assert.AreEqual(expectedLog, JsonSerializer.Serialize(_logger.Messages));
	}

	[DataTestMethod]
	[DataRow("0", "0", DsmrUnit.None)]
	[DataRow("0000", "0", DsmrUnit.None)]
	[DataRow("4.2", "4.2", DsmrUnit.None)]
	[DataRow("0042.4200", "42.42", DsmrUnit.None)]
	[DataRow("0.42", "0.42", DsmrUnit.None)]
	[DataRow("42.0", "42.0", DsmrUnit.None)]
	[DataRow("42", "42", DsmrUnit.None)]
	[DataRow("42*kWh", "42", DsmrUnit.kWh)]
	[DataRow("42*kvarh", "42", DsmrUnit.kvarh)]
	[DataRow("42*kW", "42", DsmrUnit.kW)]
	[DataRow("42*kvar", "42", DsmrUnit.kvar)]
	[DataRow("42*Hz", "42", DsmrUnit.Hz)]
	[DataRow("42*V", "42", DsmrUnit.V)]
	[DataRow("42*A", "42", DsmrUnit.A)]
	public void TestTryParseNumber(string input, string expectedData, DsmrUnit unit)
	{
		byte[] bytes = Encoding.Latin1.GetBytes(input);

		Assert.AreEqual(true, DsmrParser.TryParseNumber(bytes, unit, out ReadOnlySpan<byte> number));

		Assert.AreEqual(expectedData, Encoding.Latin1.GetString(number));
	}

	[DataTestMethod]
	[DataRow("42", DsmrUnit.kW)]
	[DataRow("42*kWh", DsmrUnit.kW)]
	[DataRow("42*kvarh", DsmrUnit.kvar)]
	[DataRow("42*kW", DsmrUnit.kWh)]
	[DataRow("42*kvar", DsmrUnit.kvarh)]
	[DataRow("42*Hz", DsmrUnit.A)]
	[DataRow("42*V", DsmrUnit.A)]
	[DataRow("42*A", DsmrUnit.V)]
	public void TestTryParseNumberFailure(string input, DsmrUnit unit)
	{
		byte[] bytes = Encoding.Latin1.GetBytes(input);

		Assert.AreEqual(false, DsmrParser.TryParseNumber(bytes, unit, out ReadOnlySpan<byte> number));

		Assert.IsTrue(number == default);
	}

	[DataTestMethod]
	[DataRow("230821112430S", "2023-08-21T11:24:30.0000000")]
	public void TestTryParseTime(string input, string expectedTime)
	{
		Assert.IsTrue(DsmrParser.TryParseTime(Encoding.Latin1.GetBytes(input), out DateTimeOffset? time));

		Assert.AreEqual(new DateTimeOffset(DateTime.ParseExact(expectedTime, "O", null)), time);
	}

	[DataTestMethod]
	[DataRow("20230821112430S")]
	[DataRow("230821112430s")]
	[DataRow(" 30821112430S")]
	[DataRow("A30821112430S")]
	[DataRow("230021112430S")]
	[DataRow("231321112430S")]
	[DataRow("230800112430S")]
	[DataRow("230832112430S")]
	[DataRow("230821242430S")]
	[DataRow("230821116030S")]
	[DataRow("230821112460S")]
	public void TestTryParseTimeFailure(string input)
	{
		Assert.IsFalse(DsmrParser.TryParseTime(Encoding.Latin1.GetBytes(input), out DateTimeOffset? time));

		Assert.IsNull(time);
	}
}
