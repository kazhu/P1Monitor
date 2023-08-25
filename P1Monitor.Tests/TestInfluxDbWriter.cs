namespace P1Monitor.Tests;

public class TestInfluxDbWriter : IInfluxDbWriter
{
	public List<DsmrValue[]> Values { get; } = new();

	public void Insert(DsmrValue[] values)
	{
		Values.Add(values.Select(x => (DsmrValue)(x switch { 
			DsmrIgnoredValue ignoredValue => new DsmrIgnoredValue(x.Mapping),
			DsmrStringValue stringValue => new DsmrStringValue(x.Mapping, stringValue.Value),
			DsmrNumberValue numberValue => new DsmrNumberValue(x.Mapping, numberValue.Value),
			DsmrTimeValue timeValue => new DsmrTimeValue(x.Mapping, timeValue.Value!.Value),
			DsmrOnOffValue onOffValue => new DsmrOnOffValue(x.Mapping, onOffValue.Value),
			_ => throw new NotImplementedException()
		})).ToArray());
	}
}
