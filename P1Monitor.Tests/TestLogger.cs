using Microsoft.Extensions.Logging;

namespace P1Monitor.Tests;

public class TestLogger<T> : ILogger<T>
{
	public Dictionary<LogLevel, List<string>> Messages { get; } = new();

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		throw new NotImplementedException();
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		Messages.TryAdd(logLevel, new List<string>());
		Messages[logLevel].Add(formatter(state, exception));
	}
}
