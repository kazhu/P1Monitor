using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;
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
			})
			.ConfigureServices((hostContext, services) =>
			{
				services.Configure<InfluxDbOptions>(hostContext.Configuration.GetSection("InfluxDb"));
				services.Configure<DsmrReaderOptions>(hostContext.Configuration.GetSection("DsmrReader"));
				services.AddSingleton<IInfluxDbWriter, InfluxDbWriter>();
				services.AddHostedService<DsmrReader>();
			})
			.Build()
			.RunAsync();
	}
}