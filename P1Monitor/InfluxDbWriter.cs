using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

public class InfluxDbWriter : BackgroundService, IInfluxDbWriter
{
	private readonly ILogger<InfluxDbWriter> _logger;
	private readonly InfluxDbOptions _options;
	private readonly HttpClient _client = new();
	private readonly string _requestUri;
	private readonly MediaTypeHeaderValue _mediaTypeHeaderValue = new("text/plain", Encoding.UTF8.WebName);
	private readonly BlockingCollection<TrimmedMemory> _blockingCollection = new();

	public InfluxDbWriter(ILogger<InfluxDbWriter> logger, IOptions<InfluxDbOptions> options)
	{
		_logger = logger;
		_options = options.Value;
		_client.BaseAddress = new Uri(_options.BaseUrl);
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.Token);
		_requestUri = $"api/v2/write?org={_options.Organization}&bucket={_options.Bucket}&precision=s";
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		return Task.Run(() => Run(stoppingToken), stoppingToken);
	}

	private void Run(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				foreach (TrimmedMemory data in _blockingCollection.GetConsumingEnumerable(stoppingToken))
				{
					using (data)
					{
						using var content = new ReadOnlyMemoryContent(data.Memory);
						content.Headers.ContentType = _mediaTypeHeaderValue;
						using var request = new HttpRequestMessage(HttpMethod.Post, _requestUri) { Content = content };
						using HttpResponseMessage response = _client.Send(request, stoppingToken);
						if (!response.IsSuccessStatusCode)
						{
							using var streamReader = new StreamReader(response.Content.ReadAsStream());
							_logger.LogError("Error writing to InfluxDB: {StatusCode} {ReasonPhrase} {message}", response.StatusCode, response.ReasonPhrase, streamReader.ReadToEnd());
						}
						else
						{
							_logger.LogDebug("Wrote {Length} long values to InfluxDB", data.Memory.Length);
						}
					}
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// Ignore, becase we are stopping
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error writing to InfluxDB");
			}
		}
		_logger.LogInformation("InfluxDB writer stopped");
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
			span = span.Append("p1value");
			foreach (ObisMapping mapping in ObisMapping.Tags)
			{
				span = span
					.Append(',')
					.Append(mapping.FieldName)
					.Append('=')
					.Append(values[mapping.Index].Data);
			}
			if (mappingGroup.Key != nameof(P1Unit.None))
			{
				span = span
					.Append(",unit=")
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
					.Append(values[mapping.Index].Data);
			}

			span = span
				.Append(' ')
				.Append(timeValue.Time!.Value.ToUnixTimeSeconds())
				.Append('\n');
		}

		ReadOnlySpan<byte> result = original[..^span.Length];
		return new TrimmedMemory(result);
	}
}

public static class SpanExtensions
{
	public static Span<byte> Append(this Span<byte> span, TrimmedMemory data)
	{
		data.Memory.Span.CopyTo(span);
		return span[data.Memory.Length..];
	}

	public static Span<byte> Append(this Span<byte> span, char value)
	{
		span[0] = (byte)value;
		return span[1..];
	}

	public static Span<byte> Append(this Span<byte> span, string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			span[i] = (byte)value[i];
		}
		return span[value.Length..];
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
			return span[written..];
		}
		return Append(span, data.ToString(CultureInfo.InvariantCulture));
	}
}
