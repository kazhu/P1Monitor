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
				using HttpResponseMessage response = await client.PostAsync(requestUri, new StringContent(content), stoppingToken);
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
		var builder = new StringBuilder();
		P1Value? timeValue = values.SingleOrDefault(x => x.P1Type == P1Type.Time && x.FieldName == "time");
		List<P1Value> tagValues = values.Where(x => x.P1Type is P1Type.String or P1Type.OnOff).OrderBy(x => x.FieldName).ToList();
		foreach (var group in values.Where(x => x.P1Type == P1Type.Number).GroupBy(x => x.Unit))
		{
			builder.Append("p1value");
			foreach (P1Value tagValue in tagValues)
			{
				builder
					.Append(',')
					.Append(tagValue.FieldName)
					.Append('=')
					.Append(tagValue.Data);
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
			foreach (P1Value tagValue in group)
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
					.Append(tagValue.Data);
			}

			if (timeValue != null)
			{
				builder
					.Append(' ')
					.Append(timeValue.Value.Time!.Value.ToUnixTimeSeconds())
					.Append('\n');
			}
		}
		return builder.ToString();
	}
}
