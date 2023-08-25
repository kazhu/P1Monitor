using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using P1Monitor.Model;

namespace P1Monitor.Tests;

[TestClass]
public class DsmrParserTest
{
	private readonly TestLogger<DsmrParser> _logger;
	private readonly IObisMappingsProvider _obisMappingProvider = new TestObisMappingsProvider();
	private readonly DsmrParser _parser;
	private readonly DsmrValue[] _values;

	public DsmrParserTest()
	{
		_logger = new TestLogger<DsmrParser>();
		_parser = new DsmrParser(_logger, _obisMappingProvider);
		_values = new DsmrValue[_obisMappingProvider.Mappings.Count];
		foreach (ObisMapping mapping in _obisMappingProvider.Mappings)
		{
			_values[mapping.Index] = DsmrValue.Create(mapping);
		}
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
	[DataRow("0-0:1.0.0(230817171430S)", "time", "2023-08-17T17:14:30.0000000")]
	[DataRow("0-0:1.0.0(230817171430S)\r\n", "time", "2023-08-17T17:14:30.0000000")]
	[DataRow("0-0:1.0.0(230817171430S)\r\nremaining", "time", "2023-08-17T17:14:30.0000000", "remaining")]
	[DataRow("0-0:42.0.0(AUX1030303218166)", "name", "AUX1030303218166")]
	[DataRow("0-0:96.50.68(ON)", "state", "ON")]
	[DataRow("0-0:96.14.0(0001)", "tariff", "1")]
	[DataRow("1-0:13.7.0(0.336)", "power_factor", "0.336")]
	[DataRow("0-0:98.1.0(230801000000S)(000663.924*kWh)", "previous_month", "")]
	public void TestParseDataLine(string input, string fieldName, string expectedValue, string remaining = "")
	{
		var inputBytes = new ReadOnlySpan<byte>(Encoding.Latin1.GetBytes(input));

		DsmrValue? result = _parser.ParseDataLine(ref inputBytes, _values);

		Assert.AreEqual(remaining, Encoding.Latin1.GetString(inputBytes));
		Assert.IsNotNull(result);
		Assert.IsFalse(result.IsEmpty);
		Assert.IsNotNull(result.Mapping);
		Assert.AreEqual(fieldName, result.Mapping.FieldName);
		switch (result.Mapping.DsmrType)
		{
			case DsmrType.Ignored:
				Assert.IsInstanceOfType<DsmrIgnoredValue>(result);
				break;
			case DsmrType.String:
				Assert.IsInstanceOfType<DsmrStringValue>(result);
				Assert.AreEqual(expectedValue, ((DsmrStringValue)result).Value);
				break;
			case DsmrType.Number:
				Assert.IsInstanceOfType<DsmrNumberValue>(result);
				Assert.AreEqual(decimal.Parse(expectedValue), ((DsmrNumberValue)result).Value);
				break;
			case DsmrType.Time:
				Assert.IsInstanceOfType<DsmrTimeValue>(result);
				Assert.AreEqual(new DateTimeOffset(DateTime.ParseExact(expectedValue, "O", null)), ((DsmrTimeValue)result).Value);
				break;
			case DsmrType.OnOff:
			default:
				Assert.IsInstanceOfType<DsmrOnOffValue>(result);
				Assert.AreEqual(Enum.Parse<DsmrOnOffValue.OnOff>(expectedValue), ((DsmrOnOffValue)result).Value);
				break;
		}
		_logger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_logger.Messages));
	}

	[DataTestMethod]
	[DataRow("0-0:96.50.68ON)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"0-0:96.50.68ON): not well formed, dropped\"]}")]
	[DataRow("0-0:96.50.68ON)", "", "{\"Error\":[\"0-0:96.50.68ON): not well formed, dropped\"]}")]
	[DataRow("(ON)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"(ON): not well formed, dropped\"]}")]
	[DataRow("0-0:0.0.0(ON)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Warning\":[\"0-0:0.0.0(ON): unknown obis id, line dropped\"]}")]
	[DataRow("0-0:96.50.68(on)\r\n0-0:96.14.0(0000)", "0-0:96.14.0(0000)", "{\"Error\":[\"0-0:96.50.68(on): parsing of value failed\"]}")]
	public void TestParseDataLineFailure(string input, string remaining, string expectedLog)
	{
		var inputBytes = new ReadOnlySpan<byte>(Encoding.Latin1.GetBytes(input));

		DsmrValue? result = _parser.ParseDataLine(ref inputBytes, _values);

		Assert.AreEqual(remaining, Encoding.Latin1.GetString(inputBytes));
		if (result != null)
		{
			Assert.IsTrue(result.IsEmpty);
		}
		Assert.AreEqual(expectedLog, JsonSerializer.Serialize(_logger.Messages));
	}

	[TestMethod]
	public void TestParseDataLineDuplicatedValue()
	{
		ReadOnlySpan<byte> inputBytes = "0-0:96.50.68(ON)\r\n0-0:96.50.68(ON)"u8;

		DsmrValue? result = _parser.ParseDataLine(ref inputBytes, _values);
		Assert.IsNotNull(result);
		Assert.IsFalse(result.IsEmpty);
		_logger.Messages.Remove(LogLevel.Trace);
		Assert.AreEqual("{}", JsonSerializer.Serialize(_logger.Messages));

		result = _parser.ParseDataLine(ref inputBytes, _values);
		Assert.IsNotNull(result);
		Assert.IsTrue(result.IsEmpty);
		Assert.AreEqual("{\"Error\":[\"0-0:96.50.68(ON): duplicated value\"]}", JsonSerializer.Serialize(_logger.Messages));
	}
}
