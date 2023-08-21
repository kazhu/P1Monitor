namespace P1Monitor.Tests;

public class TestInfluxDbWriter : IInfluxDbWriter
{
	public List<P1Value[]> Values { get; } = new();

	public void Insert(P1Value[] values)
	{
		Values.Add(values.Select(x => x with { Data = new TrimmedMemory(x.Data.Memory.Span) } ).ToArray());
	}
}
