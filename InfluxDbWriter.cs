using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;

namespace P1Monitor;

public class InfluxDbWriter : BackgroundService
{
	private readonly ILogger<InfluxDbWriter> _logger;
	private readonly ChannelReader<List<P1Value>> _valuesReader;
	private readonly InfluxDbOptions _options;

	public InfluxDbWriter(ILogger<InfluxDbWriter> logger, ChannelReader<List<P1Value>> valuesReader, IOptions<InfluxDbOptions> options)
	{
		_logger = logger;
		_valuesReader = valuesReader;
		_options = options.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var client = new HttpClient();
		client.BaseAddress = new Uri(_options.BaseUrl);
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.Token);
		string requestUri = $"api/v2/write?org={_options.Organization}&bucket={_options.Bucket}&precision=s";

		while (true)
		{
			try
			{
				List<P1Value> values = await _valuesReader.ReadAsync(stoppingToken);
				string content = GenerateLines(values);
				HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(content), stoppingToken);
				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError("Error writing to InfluxDB: {StatusCode} {ReasonPhrase} {message}", response.StatusCode, response.ReasonPhrase, await response.Content.ReadAsStringAsync());
				}
				else
				{
					_logger.LogDebug("Wrote {Count} values to InfluxDB", values.Count);
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "Error writing to InfluxDB");
			}
		}
	}

	private static string GenerateLines(List<P1Value> values)
	{
		var timeValue = values.OfType<P1TimeValue>().Single(x => x.FieldName == "time");
		var builder = new StringBuilder();
		foreach (var group in values.OfType<P1NumberValue>().GroupBy(x => x.Unit))
		{
			builder.Append("p1value");
			foreach (var tagValue in values.OfType<P1StringValue>().OrderBy(x => x.FieldName))
			{
				builder
					.Append(',')
					.Append(tagValue.FieldName)
					.Append('=')
					.Append(tagValue.Value);
			}
			if (group.Key != P1Unit.None)
			{
				builder
					.Append(",unit=")
					.Append(group.Key)
					.Append(' ');
			}
			builder.Append(' ');

			bool isFirst = true;
			foreach (P1NumberValue tagValue in group.OrderBy(x => x.FieldName))
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

			builder
				.Append(' ')
				.Append(timeValue.Value.ToUnixTimeSeconds())
				.Append('\n');
		}
		return builder.ToString();
	}
}
