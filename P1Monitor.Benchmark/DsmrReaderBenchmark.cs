﻿using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace P1Monitor.Benchmark;

[MemoryDiagnoser]
public class DsmrReaderBenchmark
{
	private readonly DsmrReaderOptions _options;
	private readonly DsmrReader _reader;
	private readonly TrimmedMemory[] _lines;

	public DsmrReaderBenchmark()
	{
		_options = new DsmrReaderOptions { Host = "localhost", Port = 2323 };
		_reader = new DsmrReader(NullLogger<DsmrReader>.Instance, new NullInfluxDbWriter(), Options.Create(_options));
		_lines = File.ReadAllLines("lines.txt", Encoding.Latin1).Select(x => TrimmedMemory.Create(Encoding.Latin1.GetBytes(x))).ToArray();
	}

	private class NullInfluxDbWriter : IInfluxDbWriter
	{
		public void Insert(P1Value[] values) { }
	}


	[Benchmark]
	public void Test()
	{
		var state = DsmrReader.State.Starting;
		foreach (var line in _lines)
		{
			state = _reader.ProcessLine(line.Span, state);
		}
	}

}
