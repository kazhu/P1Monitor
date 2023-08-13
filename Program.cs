using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace P1Monitor;

public partial class Program
{
	public static async Task Main(string[] args)
	{
		HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
		builder.Environment.ContentRootPath = Directory.GetCurrentDirectory();
		builder.Configuration.AddJsonFile("appsettings.json", optional: true);
		builder.Configuration.AddEnvironmentVariables(prefix: "P1Monitor");
		builder.Configuration.AddCommandLine(args);
		builder.Services.AddSingleton<ConcurrentQueue<List<P1Value>>>();
		builder.Services.Configure<InfluxDbOptions>(builder.Configuration.GetSection("InfluxDb"));
		builder.Services.Configure<DsmrReaderOptions>(builder.Configuration.GetSection("DsmrReader"));
		builder.Services.AddHostedService<DsmrReader>();
		builder.Services.AddHostedService<InfluxDbWriter>();

		IHost host = builder.Build();
		await host.RunAsync();
	}
}