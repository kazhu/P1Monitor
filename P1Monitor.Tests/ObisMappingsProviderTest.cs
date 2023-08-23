using Microsoft.Extensions.Logging.Abstractions;
using P1Monitor.Model;
using P1Monitor.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace P1Monitor.Tests;

[TestClass]
public class ObisMappingsProviderTest
{
	private const string json =
		"""
		{
			"EON_HU_SX631": {
				"country": "Hungary",
				"vendor": "E.ON",
				"source": "https://www.eon.hu/content/dam/eon/eon-hungary/documents/Muszaki-ugyek/Fogyasztasmerok-leirasa/sanxing-sx6x1/EON-leiras-az-ugyfelek-szamara-SX6X1-S12U16-S34U18-V010.pdf",
				"mapping": {
					"0-0:1.0.0": {
						"fieldName": "time",
						"type": "Time",
						"unit": "A"
					}
				}
			}
		}
		""";
	private const string DeviceName = "EON_HU_SX631";

	[TestMethod]
	public void TestWithRelativeFile()
	{
		const string Filename = "myobismappings.json";

		File.WriteAllText(Filename, json);
		try
		{
			var options = new ObisMappingsOptions { DeviceName = DeviceName, MappingFile = Filename };

			ObisMappingList data = new ObisMappingsProvider(NullLogger<ObisMappingsProvider>.Instance, OptionsFactory.Create(options)).Mappings;

			ValidateData(data);
		}
		finally
		{
			File.Delete(Filename);
		}
	}

	[TestMethod]
	public void TestWithAbsoluteFile()
	{
		string Filename = Path.Combine(Environment.CurrentDirectory, "myobismappings.json");

		File.WriteAllText(Filename, json);
		try
		{
			var options = new ObisMappingsOptions { DeviceName = DeviceName, MappingFile = Filename };

			ObisMappingList data = new ObisMappingsProvider(NullLogger<ObisMappingsProvider>.Instance, OptionsFactory.Create(options)).Mappings;

			ValidateData(data);
		}
		finally
		{
			File.Delete(Filename);
		}
	}

	private static void ValidateData(ObisMappingList data)
	{
		Assert.AreEqual(1, data.Count);
		Assert.AreEqual("0-0:1.0.0", data[0].Id);
		Assert.AreEqual("time", data[0].FieldName);
		Assert.AreEqual(DsmrType.Time, data[0].P1Type);
		Assert.AreEqual(DsmrUnit.A, data[0].Unit);
	}
}
