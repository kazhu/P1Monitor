using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P1Monitor.Model;
using P1Monitor.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace P1Monitor;

public interface IInfluxDbWriter
{
	void Insert(DsmrValue[] values);
}

public class InfluxDbWriter : BackgroundService, IInfluxDbWriter
{
	private readonly ILogger<InfluxDbWriter> _logger;
	private readonly IObisMappingsProvider _obisMappingProvider;
	private readonly InfluxDbOptions _options;
	private readonly HttpClient _client = new();
	private readonly string _requestUri;
	private readonly MediaTypeHeaderValue _mediaTypeHeaderValue = new("text/plain", Encoding.UTF8.WebName);
	private readonly BlockingCollection<(byte[] buffer, int length)> _blockingCollection = [];

	public InfluxDbWriter(ILogger<InfluxDbWriter> logger, IObisMappingsProvider obisMappingProvider, IOptions<InfluxDbOptions> options)
	{
		_logger = logger;
		_obisMappingProvider = obisMappingProvider;
		_options = options.Value;
		_client.BaseAddress = new Uri(_options.BaseUrl);
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.Token);
		_requestUri = $"api/v2/write?org={_options.Organization}&bucket={_options.Bucket}&precision=s";
	}

	public void Insert(DsmrValue[] values)
	{
		_blockingCollection.Add(GenerateLines(values));
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
				foreach ((byte[] buffer, int length) in _blockingCollection.GetConsumingEnumerable(stoppingToken))
				{
					try
					{
						using var content = new StreamContent(new MemoryStream(buffer, 0, length, false, true), length);
						content.Headers.ContentType = _mediaTypeHeaderValue;
						using var request = new HttpRequestMessage(HttpMethod.Post, _requestUri) { Content = content };
						using HttpResponseMessage response = _client.Send(request, stoppingToken);
						if (!response.IsSuccessStatusCode)
						{
							using var streamReader = new StreamReader(response.Content.ReadAsStream(stoppingToken));
							_logger.LogError("Error writing to InfluxDB: {StatusCode} {ReasonPhrase} {message}", response.StatusCode, response.ReasonPhrase, streamReader.ReadToEnd());
						}
						else
						{
							_logger.LogDebug("Wrote {Length} long values to InfluxDB", length);
						}
					}
					finally
					{
						ArrayPool<byte>.Shared.Return(buffer);
					}
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// Ignore, because we are stopping
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error writing to InfluxDB");
			}
		}
		_logger.LogInformation("InfluxDB writer stopped");
	}

	internal (byte[], int) GenerateLines(DsmrValue[] values)
	{
		int length = CalculateLength(values);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
		try
		{
			ReadOnlySpan<byte> result = WriteValues(values, buffer);
			return (buffer, result.Length);
		}
		catch
		{
			ArrayPool<byte>.Shared.Return(buffer);
			throw;
		}
	}

	private int CalculateLength(DsmrValue[] values)
	{
		int length = 0;
		foreach (UnitNumberMappings unitNumberMappings in _obisMappingProvider.Mappings.NumberMappingsByUnit)
		{
			length += ("p1value" + ",unit=" + " " + " " + "\n").Length + 10/*epoch seconds*/ + unitNumberMappings.Unit.Length;
			foreach (ObisMapping mapping in _obisMappingProvider.Mappings.Tags)
			{
				length += mapping.FieldName.Length + 2;
				length += values[mapping.Index] switch
				{
					DsmrStringValue stringValue => Encoding.Latin1.GetByteCount(stringValue.Value),
					DsmrOnOffValue onOffValue => onOffValue.Value == DsmrOnOffValue.OnOff.ON ? 2 : 3,
					_ => throw new NotSupportedException($"Unsupported type {values[mapping.Index].GetType().Name}"),
				};
			}
			foreach (ObisMapping mapping in unitNumberMappings.Mappings)
			{
				length += mapping.FieldName.Length + 64/*decimal max length*/ + 2;
			}
		}

		return length;
	}

	private ReadOnlySpan<byte> WriteValues(DsmrValue[] values, Span<byte> buffer)
	{
		DateTimeOffset time = (_obisMappingProvider.Mappings.TimeField is null ? null : ((DsmrTimeValue)values[_obisMappingProvider.Mappings.TimeField.Index]).Value) ?? DateTimeOffset.Now;
		Span<byte> span = buffer;
		foreach (UnitNumberMappings unitNumberMappings in _obisMappingProvider.Mappings.NumberMappingsByUnit)
		{
			span = span.Append("p1value");
			foreach (ObisMapping mapping in _obisMappingProvider.Mappings.Tags)
			{
				span = span
					.Append(',')
					.Append(mapping.FieldName)
					.Append('=')
					.Append(values[mapping.Index]);
			}
			if (unitNumberMappings.Unit != nameof(DsmrUnit.None))
			{
				span = span
					.Append(",unit=")
					.Append(unitNumberMappings.Unit);
			}
			span = span.Append(' ');

			bool isFirst = true;
			foreach (ObisMapping mapping in unitNumberMappings.Mappings)
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
					.Append(values[mapping.Index]);
			}

			span = span
				.Append(' ')
				.Append(time.ToUnixTimeSeconds())
				.Append('\n');
		}

		return buffer[..^span.Length];
	}
}

public static class SpanExtensions
{
	public static Span<byte> Append(this Span<byte> span, DsmrValue value)
	{
        switch (value)
        {
            case DsmrNumberValue numberValue:
                {
                    Span<char> chars = stackalloc char[64];
                    numberValue.Value.TryFormat(chars, out int dataWritten, format: null, provider: CultureInfo.InvariantCulture);
                    Encoding.Latin1.GetBytes(chars[..dataWritten], span[..dataWritten]);
                    return span[dataWritten..];
                }

            case DsmrStringValue stringValue:
                {
                    int length = Encoding.Latin1.GetByteCount(stringValue.Value);
                    Encoding.Latin1.GetBytes(stringValue.Value, span[..length]);
                    return span[length..];
                }

            case DsmrOnOffValue onOffValue:
                if (onOffValue.Value == DsmrOnOffValue.OnOff.ON)
                {
                    "ON"u8.CopyTo(span);
                    return span[2..];
                }
                else
                {
                    "OFF"u8.CopyTo(span);
                    return span[3..];
                }

            default:
                throw new NotSupportedException($"Unsupported type {value.GetType().Name}");
        }
    }

	public static Span<byte> Append(this Span<byte> span, char value)
	{
		span[0] = (byte)value;
		return span[1..];
	}

	public static Span<byte> Append(this Span<byte> span, string value)
	{
		int length = Encoding.Latin1.GetByteCount(value);
		Encoding.Latin1.GetBytes(value, span[..length]);
		return span[length..];
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
