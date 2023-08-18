using System.Threading.Channels;

namespace P1Monitor.Tests;

public class TestInfluxDbWriter : IInfluxDbWriter
{
	public List<P1Value[]> Values { get; } = new();

	public Task InsertAsync(P1Value[] values, CancellationToken cancellationToken)
	{
		Values.Add(values);
		return Task.CompletedTask;
	}
}
