using P1Monitor.Model;
using System.Text;

namespace P1Monitor.Tests;

[TestClass]
public class ObisMappingListTest
{
	[TestMethod]
	public void TestList()
	{
		var obismappinglist = new ObisMappingList(TestObisMappingsProvider.TestMappings);

		Assert.AreEqual(TestObisMappingsProvider.TestMappings.Length, obismappinglist.Count);
		for (int i = 0; i < TestObisMappingsProvider.TestMappings.Length; i++)
		{
			Assert.AreEqual(TestObisMappingsProvider.TestMappings[i], obismappinglist[i], $"{TestObisMappingsProvider.TestMappings[i]} vs. {obismappinglist[i]}");
		}
	}

	[TestMethod]
	public void TestTags()
	{
		var obismappinglist = new ObisMappingList(TestObisMappingsProvider.TestMappings);

		ObisMapping[] expectedTags = TestObisMappingsProvider.TestMappings.Where(x => x.DsmrType == DsmrType.String || x.DsmrType == DsmrType.OnOff).OrderBy(x => x.FieldName).ToArray();
		CollectionAssert.AreEqual(expectedTags, obismappinglist.Tags);
	}

	[TestMethod]
	public void TestTimeField()
	{
		var obismappinglist = new ObisMappingList(TestObisMappingsProvider.TestMappings);

		Assert.AreEqual(TestObisMappingsProvider.TestMappings[0], obismappinglist.TimeField);
	}

	[TestMethod]
	public void TestGetEnumerator()
	{
		var obismappinglist = new ObisMappingList(TestObisMappingsProvider.TestMappings);

		int i = 0;
		foreach (ObisMapping mapping in obismappinglist)
		{
			Assert.AreEqual(TestObisMappingsProvider.TestMappings[i++], mapping);
		}

		Assert.AreEqual(TestObisMappingsProvider.TestMappings.Length, i);
	}

	[TestMethod]
	public void TestTryGetMappingById()
	{
		var obismappinglist = new ObisMappingList(TestObisMappingsProvider.TestMappings);

		foreach (ObisMapping expectedMapping in TestObisMappingsProvider.TestMappings)
		{
			Assert.IsTrue(obismappinglist.TryGetMappingById(Encoding.Latin1.GetBytes(expectedMapping.Id), out ObisMapping? mapping), $"Looking up {expectedMapping.Id}");
			Assert.AreEqual(expectedMapping, mapping, $"{expectedMapping} vs. {mapping}");
		}

		Assert.IsFalse(obismappinglist.TryGetMappingById(""u8, out _));
		Assert.IsFalse(obismappinglist.TryGetMappingById(" "u8, out _));
		Assert.IsFalse(obismappinglist.TryGetMappingById("x"u8, out _));
		Assert.IsFalse(obismappinglist.TryGetMappingById("0-0:96.50.68999999"u8, out _));
	}

	[TestMethod]
	public void TestNumberMappingsByUnit()
	{
		var obismappinglist = new ObisMappingList(TestObisMappingsProvider.TestMappings);

		var expectedNumberMappingsByUnit = TestObisMappingsProvider.TestMappings.Where(x => x.DsmrType == DsmrType.Number).GroupBy(x => x.Unit).ToList();
		Assert.AreEqual(expectedNumberMappingsByUnit.Count, obismappinglist.NumberMappingsByUnit.Length);
		for (int i = 0; i < expectedNumberMappingsByUnit.Count; i++)
		{
			Assert.AreEqual(expectedNumberMappingsByUnit[i].Key.ToString(), obismappinglist.NumberMappingsByUnit[i].Unit);
			CollectionAssert.AreEqual(expectedNumberMappingsByUnit[i].OrderBy(x => x.FieldName).ToArray(), obismappinglist.NumberMappingsByUnit[i].Mappings);
		}
	}
}
