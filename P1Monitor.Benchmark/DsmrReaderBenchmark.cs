using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace P1Monitor.Benchmark;

[MemoryDiagnoser]
public class DsmrReaderBenchmark
{
	private readonly DsmrReaderOptions _options;
	private readonly DsmrParser _dsmrParser;
	private readonly DsmrReader _reader;
	private readonly byte[] _data;

	public DsmrReaderBenchmark()
	{
		_options = new DsmrReaderOptions { Host = "localhost", Port = 2323 };
		_dsmrParser = new DsmrParser(NullLogger<DsmrParser>.Instance);
		_reader = new DsmrReader(NullLogger<DsmrReader>.Instance, new NullInfluxDbWriter(), _dsmrParser, Options.Create(_options));
		_data = File.ReadAllBytes("lines.txt");
	}

	private class NullInfluxDbWriter : IInfluxDbWriter
	{
		public void Insert(P1Value[] values) { }
	}


	[Benchmark]
	public void Test()
	{
		_reader.ProcessBuffer(_data);
	}

}
