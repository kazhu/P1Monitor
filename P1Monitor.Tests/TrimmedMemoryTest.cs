using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P1Monitor.Tests;

[TestClass]
public class TrimmedMemoryTest
{

	[TestMethod]
	public void DoIt()
	{
		JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
		File.WriteAllText(
			@"c:\Users\kaz\Downloads\data.json",
			JsonSerializer.Serialize(ObisMapping.Mappings.ToDictionary(x => Encoding.Latin1.GetString(x.Id.Memory.Span), x => new { x.FieldName, Type = x.P1Type, x.Unit }), options)
		); 
	}

	[TestMethod]
	public void TestEquals()
	{
		using var memory1 = new TrimmedMemory("42"u8);
		using var memory2 = new TrimmedMemory("421"u8[..2]);

		var obj = memory2 as object;

		Assert.IsTrue(memory1.Equals(memory2));
		Assert.IsTrue(memory1.Equals(obj));
	}

	[TestMethod]
	public void TestOperatorEqual()
	{
		using var memory1 = new TrimmedMemory("42"u8);
		using var memory2 = new TrimmedMemory("421"u8[..2]);

		Assert.IsTrue(memory1 == memory2);
		Assert.IsFalse(memory1 != memory2);
	}

	[TestMethod]
	public void TestGetHashCode()
	{
		using var memory1 = new TrimmedMemory("42"u8);
		using var memory2 = new TrimmedMemory("421"u8[..2]);

		Assert.AreEqual(memory1.GetHashCode(), memory2.GetHashCode());
	}

	[TestMethod]
	public void TestToString()
	{
		using var memory1 = new TrimmedMemory("42"u8);

		Assert.AreEqual("42", memory1.ToString());
	}

	[TestMethod]
	public void TestMemory()
	{
		using var memory1 = new TrimmedMemory("42"u8);

		Assert.AreEqual("42", Encoding.Latin1.GetString(memory1.Memory.Span));
	}
}
