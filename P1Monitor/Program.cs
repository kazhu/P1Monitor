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
				config.Sources.Clear(); // removing default configuration sources to get rid of the default AddJsonFile calls to avoid high CPU usage on Linux
				AddConfigFile(config, configPath, "appsettings.json");
				AddConfigFile(config, configPath, $"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json");
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

	private static void AddConfigFile(IConfigurationBuilder config, string configPath, string fileName)
	{
		var path = Path.Combine(configPath, fileName);
		if (File.Exists(path))
		{
			// Uses AddJsonStream instead of AddJsonFile, which yields usage of FileSystemWatcher indirectly, which causes high CPU usage on Linux
			config.AddJsonStream(new MemoryStream(File.ReadAllBytes(path)));
		}
	}
}