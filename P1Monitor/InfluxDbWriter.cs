﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace P1Monitor;

public interface IInfluxDbWriter
{
	void Insert(P1Value[] values);
}

public class InfluxDbWriter : IInfluxDbWriter
{
	private readonly ILogger<InfluxDbWriter> _logger;
	private readonly InfluxDbOptions _options;
	private readonly HttpClient _client = new HttpClient();
	private readonly string _requestUri;
	private readonly MediaTypeHeaderValue _mediaTypeHeaderValue = new MediaTypeHeaderValue("text/plain", Encoding.UTF8.WebName);
	private readonly BlockingCollection<TrimmedMemory> _blockingCollection = new();

	public InfluxDbWriter(ILogger<InfluxDbWriter> logger, IOptions<InfluxDbOptions> options)
	{
		_logger = logger;
		_options = options.Value;
		_client.BaseAddress = new Uri(_options.BaseUrl);
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.Token);
		_requestUri = $"api/v2/write?org={_options.Organization}&bucket={_options.Bucket}&precision=s";
		new Thread(InsertLines) { IsBackground = true }.Start();
	}

	private void InsertLines()
	{
		try
		{
			foreach (var data in _blockingCollection.GetConsumingEnumerable())
			{
				using (data)
				{
					using ReadOnlyMemoryContent content = new ReadOnlyMemoryContent(data.Memory);
					content.Headers.ContentType = _mediaTypeHeaderValue;
					using var request = new HttpRequestMessage(HttpMethod.Post, _requestUri) { Content = content };
					using HttpResponseMessage response = _client.Send(request);
					if (!response.IsSuccessStatusCode)
					{
						using var streamReader = new StreamReader(response.Content.ReadAsStream());
						_logger.LogError("Error writing to InfluxDB: {StatusCode} {ReasonPhrase} {message}", response.StatusCode, response.ReasonPhrase, streamReader.ReadToEnd());
					}
					else
					{
						_logger.LogDebug("Wrote {Length} long values to InfluxDB", data.Length);
					}
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Error writing to InfluxDB");
		}
	}


	public void Insert(P1Value[] values)
	{
		_blockingCollection.Add(GenerateLines(values));
	}

	private static TrimmedMemory GenerateLines(P1Value[] values)
	{
		Span<byte> span = stackalloc byte[4096]; // 4096 is more than enough to hold all lines
		Span<byte> original = span;
		P1Value timeValue = values[ObisMapping.MappingByFieldName["time"].Index];
		foreach (var mappingGroup in ObisMapping.NumberMappingsByUnit)
		{
			span = span.Append("p1value"u8);
			foreach (ObisMapping mapping in ObisMapping.Tags)
			{
				span = span
					.Append(',')
					.Append(mapping.FieldName)
					.Append('=')
					.Append(values[mapping.Index].Data.Span);
			}
			if (mappingGroup.Key != nameof(P1Unit.None))
			{
				span = span
					.Append(",unit="u8)
					.Append(mappingGroup.Key)
					.Append(' ');
			}
			span = span.Append(' ');

			bool isFirst = true;
			foreach (ObisMapping mapping in mappingGroup.Value)
			{
				if (isFirst)
				{
					isFirst = false;
				}
				else
				{
					span = span.Append(',');
				}
				span = span
					.Append(mapping.FieldName)
					.Append('=')
					.Append(values[mapping.Index].Data.Span);
			}

			span = span
				.Append(' ')
				.Append(timeValue.Time!.Value.ToUnixTimeSeconds())
				.Append('\n');
		}
		return TrimmedMemory.Create(original.Slice(0, original.Length - span.Length));
	}
}

public static class SpanExtensions
{
	public static Span<byte> Append(this Span<byte> span, ReadOnlySpan<byte> value)
	{
		value.CopyTo(span);
		return span.Slice(value.Length);
	}

	public static Span<byte> Append(this Span<byte> span, char value)
	{
		span[0] = (byte)value;
		return span.Slice(1);
	}

	public static Span<byte> Append(this Span<byte> span, string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			span[i] = (byte)value[i];
		}
		return span.Slice(value.Length);
	}

	public static Span<byte> Append(this Span<byte> span, long data)
	{
		Span<char> chars = stackalloc char[30];
		if (data.TryFormat(chars, out int written, format: null, provider: CultureInfo.InvariantCulture))
		{
			for (int i = 0; i < written; i++)
			{
				span[i] = (byte)chars[i];
			}
			return span.Slice(written);
		}
		return Append(span, data.ToString(CultureInfo.InvariantCulture));
	}
}
