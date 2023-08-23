using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;
using P1Monitor.Options;
using System.Diagnostics;

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
			configPath = Path.GetDirectoryName(Environment.ProcessPath)!;
		}

		await Host.CreateDefaultBuilder(args)
			.UseSystemd()
			.ConfigureAppConfiguration((hostingContext, config) =>
			{
				config.Sources.Clear(); // removing default configuration sources to get rid of the default AddJsonFile calls to avoid high CPU usage on Linux
				config.AddJsonFile(Path.Combine(configPath, "appsettings.json"), optional: true, reloadOnChange: false);
				config.AddJsonFile(Path.Combine(configPath, $"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json"), optional: true, reloadOnChange: false);
				config.AddEnvironmentVariables(prefix: "P1Monitor_");
				config.AddCommandLine(args);
			})
			.ConfigureLogging((hostingContext, logging) =>
			{
				logging.ClearProviders();
				logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
				logging.AddDebug();
				logging.AddConsole();
			})
			.ConfigureServices((hostContext, services) =>
			{
				services.Configure<DsmrReaderOptions>(hostContext.Configuration.GetSection("DsmrReader"));
				services.Configure<InfluxDbOptions>(hostContext.Configuration.GetSection("InfluxDb"));
				services.Configure<ObisMappingsOptions>(hostContext.Configuration.GetSection("ObisMapping"));

				services.AddSingleton<IDsmrParser, DsmrParser>();
				services.AddSingleton<IInfluxDbWriter, InfluxDbWriter>();
				services.AddSingleton<IObisMappingsProvider, ObisMappingsProvider>();

				services.AddHostedService<DsmrReader>();
				services.AddHostedService(provider => (InfluxDbWriter)provider.GetService<IInfluxDbWriter>()!);
			})
			.Build()
			.RunAsync();
	}
}