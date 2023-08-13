using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;

namespace P1Monitor;

public class InfluxDbWriter : BackgroundService
{
	private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(1);

	private readonly ILogger<InfluxDbWriter> _logger;
	private readonly ConcurrentQueue<List<P1Value>> _valuesQueue;
	private readonly InfluxDbOptions _options;

	public InfluxDbWriter(ILogger<InfluxDbWriter> logger, ConcurrentQueue<List<P1Value>> valuesQueue, IOptions<InfluxDbOptions> options)
	{
		_logger = logger;
		_valuesQueue = valuesQueue;
		_options = options.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (true)
		{
			try
			{
				using var client = new HttpClient();
				client.BaseAddress = new Uri(_options.BaseUrl);
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.Token);

				while (true)
				{
					if (_valuesQueue.TryDequeue(out List<P1Value>? values) || values == null)
					{
						await Task.Delay(WaitTime, stoppingToken);
						continue;
					}

					var response = await client.PostAsync($"api/v2/write?org={_options.Organization}&bucket={_options.Bucket}&precision=s", new StringContent(GenerateLine(values)), stoppingToken);
					response.EnsureSuccessStatusCode();
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "Error writing to InfluxDB");
			}
		}
	}

	private static string GenerateLine(List<P1Value> values)
	{
		var builder = new StringBuilder();
		builder.Append("p1value");
		foreach (var tagValue in values.OfType<P1StringValue>().OrderBy(x => x.FieldName))
		{
			builder
				.Append(',')
				.Append(tagValue.FieldName)
				.Append('=')
				.Append(tagValue.Value);
		}
		builder.Append(' ');
		bool isFirst = true;
		foreach (var tagValue in values.OfType<P1NumberValue>())
		{
			if (isFirst)
			{
				isFirst = false;
			}
			else
			{
				builder.Append(',');
			}
			builder
				.Append(tagValue.FieldName)
				.Append('=')
				.Append(tagValue.Value);
		}

		var timeValue = values.OfType<P1TimeValue>().Single(x => x.FieldName == "time");
		builder
			.Append(' ')
			.Append(timeValue.Value.ToUnixTimeSeconds());
		return builder.ToString();
	}
}
