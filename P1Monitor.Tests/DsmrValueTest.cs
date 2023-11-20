using P1Monitor.Model;
using System.Globalization;
using System.Text;

namespace P1Monitor.Tests;

[TestClass]
public class DsmrValueTest
{
	[DataTestMethod]
	[DataRow(DsmrType.Ignored, typeof(DsmrIgnoredValue))]
	[DataRow(DsmrType.String, typeof(DsmrStringValue))]
	[DataRow(DsmrType.Number, typeof(DsmrNumberValue))]
	[DataRow(DsmrType.Time, typeof(DsmrTimeValue))]
	[DataRow(DsmrType.OnOff, typeof(DsmrOnOffValue))]
	public void TestCreate(DsmrType dsmrType, Type expectedType)
	{
		DsmrValue ignoredValue = DsmrValue.Create(new ObisMapping("id", "field", dsmrType));
		Assert.IsInstanceOfType(ignoredValue, expectedType);
		Assert.IsTrue(ignoredValue.IsEmpty);
		Assert.IsNotNull(ignoredValue.Mapping);
		Assert.AreEqual("id field: empty", ignoredValue.ToString());
	}

	[TestMethod]
	public void TestError()
	{
		Assert.IsNotNull(DsmrValue.Error);
		Assert.IsTrue(DsmrValue.Error.IsEmpty);
	}

	[TestMethod]
	public void TestIgnoredValue()
	{
		var value = new DsmrIgnoredValue(new ObisMapping("id", "field", DsmrType.Ignored));
		Assert.AreNotEqual(value, DsmrValue.Error);
		Assert.IsTrue(value.IsEmpty);
		Assert.IsTrue(value.TrySetValue("12345678901234567890123456789012345678901234567890"u8));
		Assert.IsFalse(value.IsEmpty);
		Assert.AreEqual("id field: ignored", value.ToString());
		var value2 = new DsmrIgnoredValue(new ObisMapping("id", "field", DsmrType.Ignored));
		Assert.AreNotEqual(value, value2);
		Assert.AreNotEqual(value.GetHashCode(), value2.GetHashCode());
		value.Clear();
		Assert.IsTrue(value.IsEmpty);
		Assert.AreEqual(value, value2);
		Assert.AreEqual(value.GetHashCode(), value2.GetHashCode());
	}

	[TestMethod]
	public void TestStringValue()
	{
		var value = new DsmrStringValue(new ObisMapping("id", "field", DsmrType.String));
		Assert.AreNotEqual(value, DsmrValue.Error);
		Assert.IsTrue(value.IsEmpty);
		Assert.IsTrue(value.TrySetValue("12345678901234567890123456789012"u8));
		Assert.IsFalse(value.IsEmpty);
		Assert.AreEqual("id field: \"12345678901234567890123456789012\"", value.ToString());
		var value2 = new DsmrStringValue(new ObisMapping("id", "field", DsmrType.String), "12345678901234567890123456789012");
		Assert.AreEqual(value, value2);
		Assert.AreEqual(value.GetHashCode(), value2.GetHashCode());
		value.Clear();
		Assert.IsTrue(value.IsEmpty);
		Assert.AreNotEqual(value, value2);
		Assert.AreNotEqual(value.GetHashCode(), value2.GetHashCode());
	}

	[TestMethod]
	public void TestStringValueTooLong()
	{
		var value = new DsmrStringValue(new ObisMapping("id", "field", DsmrType.String), "12345678901234567890123456789012");

		Assert.IsFalse(value.TrySetValue("123456789012345678901234567890123"u8));

		Assert.IsTrue(value.IsEmpty);
	}

	[DataTestMethod]
	[DataRow("0", DsmrUnit.None, "0")]
	[DataRow("0000", DsmrUnit.None, "0")]
	[DataRow("4.2", DsmrUnit.None, "4.2")]
	[DataRow("0042.4200", DsmrUnit.None, "42.42")]
	[DataRow("0.42", DsmrUnit.None, "0.42")]
	[DataRow("42.0", DsmrUnit.None, "42")]
	[DataRow("42", DsmrUnit.None, "42")]
	[DataRow("42*kWh", DsmrUnit.kWh, "42")]
	[DataRow("42*kvarh", DsmrUnit.kvarh, "42")]
	[DataRow("42*kW", DsmrUnit.kW, "42")]
	[DataRow("42*kvar", DsmrUnit.kvar, "42")]
	[DataRow("42*Hz", DsmrUnit.Hz, "42")]
	[DataRow("42*V", DsmrUnit.V, "42")]
	[DataRow("42*A", DsmrUnit.A, "42")]
	public void TestNumberValue(string input, DsmrUnit unit, string expectedValueText)
	{
		var expectedValue = decimal.Parse(expectedValueText, CultureInfo.InvariantCulture);
		var value = new DsmrNumberValue(new ObisMapping("id", "field", DsmrType.Number, unit));
		Assert.AreNotEqual(value, DsmrValue.Error);
		Assert.IsTrue(value.IsEmpty);
		Assert.IsTrue(value.TrySetValue(Encoding.Latin1.GetBytes(input)));
		Assert.IsFalse(value.IsEmpty);
		Assert.AreEqual(expectedValue, value.Value);
		if (unit == DsmrUnit.None)
		{
			Assert.AreEqual($"id field: {expectedValue}", value.ToString());
		}
		else
		{
			Assert.AreEqual($"id field: {expectedValue} {unit}", value.ToString());
		}
		var value2 = new DsmrNumberValue(new ObisMapping("id", "field", DsmrType.Number, unit), expectedValue);
		Assert.AreEqual(value, value2);
		Assert.AreEqual(value.GetHashCode(), value2.GetHashCode());
		value.Clear();
		Assert.IsTrue(value.IsEmpty);
		Assert.AreNotEqual(value, value2);
		Assert.AreNotEqual(value.GetHashCode(), value2.GetHashCode());
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
	public void TestNumberValueWrongUnit(string input, DsmrUnit unit)
	{
		var value = new DsmrNumberValue(new ObisMapping("id", "field", DsmrType.Number, unit), 1);
		Assert.IsFalse(value.TrySetValue(Encoding.Latin1.GetBytes(input)));
		Assert.IsTrue(value.IsEmpty);
	}

	[TestMethod]
	public void TestTimeValue()
	{
		DateTimeOffset expectedValue = new DateTimeOffset(2023, 8, 21, 11, 24, 30, TimeSpan.FromHours(2));
		var value = new DsmrTimeValue(new ObisMapping("id", "field", DsmrType.Time));
		Assert.AreNotEqual(value, DsmrValue.Error);
		Assert.IsTrue(value.IsEmpty);
		Assert.IsTrue(value.TrySetValue("230821112430W"u8));
		Assert.AreEqual(expectedValue, value.Value);
		Assert.IsFalse(value.IsEmpty);
		Assert.IsTrue(value.TrySetValue("230821112430S"u8));
		Assert.AreEqual(expectedValue, value.Value);
		Assert.IsFalse(value.IsEmpty);
		Assert.AreEqual("id field: 2023-08-21T11:24:30.0000000+02:00", value.ToString());
		var value2 = new DsmrTimeValue(new ObisMapping("id", "field", DsmrType.Time), expectedValue);
		Assert.AreEqual(value, value2);
		Assert.AreEqual(value.GetHashCode(), value2.GetHashCode());
		value.Clear();
		Assert.IsTrue(value.IsEmpty);
		Assert.AreNotEqual(value, value2);
		Assert.AreNotEqual(value.GetHashCode(), value2.GetHashCode());
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
	public void TestTimeValueFailures(string input)
	{
		var value = new DsmrTimeValue(new ObisMapping("id", "field", DsmrType.Time), DateTimeOffset.Now);
		Assert.IsFalse(value.TrySetValue(Encoding.Latin1.GetBytes(input)));
		Assert.IsTrue(value.IsEmpty);
	}

	[DataTestMethod]
	[DataRow("ON", DsmrOnOffValue.OnOff.ON)]
	[DataRow("OFF", DsmrOnOffValue.OnOff.OFF)]
	public void TestOnOffValue(string input, DsmrOnOffValue.OnOff expectedValue)
	{
		var value = new DsmrOnOffValue(new ObisMapping("id", "field", DsmrType.OnOff));
		Assert.AreNotEqual(value, DsmrValue.Error);
		Assert.IsTrue(value.IsEmpty);
		Assert.IsTrue(value.TrySetValue(Encoding.Latin1.GetBytes(input)));
		Assert.AreEqual(expectedValue, value.Value);
		Assert.IsFalse(value.IsEmpty);
		Assert.AreEqual($"id field: {expectedValue}", value.ToString());
		var value2 = new DsmrOnOffValue(new ObisMapping("id", "field", DsmrType.OnOff), expectedValue);
		Assert.AreEqual(value, value2);
		Assert.AreEqual(value.GetHashCode(), value2.GetHashCode());
		value.Clear();
		Assert.IsTrue(value.IsEmpty);
		Assert.AreNotEqual(value, value2);
		Assert.AreNotEqual(value.GetHashCode(), value2.GetHashCode());
	}

	[TestMethod]
	public void TestOnOffValueFailure()
	{
		var value = new DsmrOnOffValue(new ObisMapping("id", "field", DsmrType.OnOff), DsmrOnOffValue.OnOff.ON);
		Assert.IsFalse(value.TrySetValue("off"u8));
		Assert.IsTrue(value.IsEmpty);
	}
}
