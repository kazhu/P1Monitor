using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Diagnostics;
using System.Threading.Channels;

namespace P1Monitor;

public partial class Program
{
	public static async Task Main(string[] args)
	{
		string configPath;
		if (SystemdHelpers.IsSystemdService())
		{
			configPath = "/etc/p1monitor/";
		}
		else
		{
			configPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
		}

		await Host.CreateDefaultBuilder(args)
			.UseSystemd()
			.ConfigureAppConfiguration((hostingContext, config) =>
			{
				config.AddJsonFile(Path.Combine(configPath, "appsettings.json"), optional: true);
				config.AddJsonFile(Path.Combine(configPath, $"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json"), optional: true);
				config.AddEnvironmentVariables(prefix: "P1Monitor_");
				config.AddCommandLine(args);
			})
			.ConfigureLogging((hostingContext, logging) =>
			{
				logging.ClearProviders();
				logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
				logging.AddDebug();
				logging.AddConsole();
				//logging.AddSystemdConsole(options => options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss ");
			})
			.ConfigureServices((hostContext, services) =>
			{
				Channel<List<P1Value>> channel = Channel.CreateBounded<List<P1Value>>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });
				services.AddSingleton(typeof(ChannelReader<List<P1Value>>), channel.Reader);
				services.AddSingleton(typeof(ChannelWriter<List<P1Value>>), channel.Writer);

				services.Configure<InfluxDbOptions>(hostContext.Configuration.GetSection("InfluxDb"));
				services.Configure<DsmrReaderOptions>(hostContext.Configuration.GetSection("DsmrReader"));
				services.AddHostedService<DsmrReader>();
				services.AddHostedService<InfluxDbWriter>();
			})
			.Build()
			.RunAsync();
	}
}