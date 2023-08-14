using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace P1Monitor;

public partial class Program
{
	public static async Task Main(string[] args)
	{
		HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

		builder.Logging.ClearProviders();
		builder.Logging.AddDebug();
		builder.Logging.AddConsole();

		builder.Environment.ContentRootPath = Directory.GetCurrentDirectory();

		builder.Configuration.AddJsonFile("appsettings.json", optional: true);
		builder.Configuration.AddEnvironmentVariables(prefix: "P1Monitor");
		builder.Configuration.AddCommandLine(args);

		Channel<List<P1Value>> channel = Channel.CreateBounded<List<P1Value>>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
		builder.Services.AddSingleton(typeof(ChannelReader<List<P1Value>>), channel.Reader);
		builder.Services.AddSingleton(typeof(ChannelWriter<List<P1Value>>), channel.Writer);

		builder.Services.Configure<InfluxDbOptions>(builder.Configuration.GetSection("InfluxDb"));
		builder.Services.Configure<DsmrReaderOptions>(builder.Configuration.GetSection("DsmrReader"));
		builder.Services.AddHostedService<DsmrReader>();
		builder.Services.AddHostedService<InfluxDbWriter>();

		IHost host = builder.Build();
		await host.RunAsync();
	}
}