namespace P1Monitor.Options;

public class DsmrReaderOptions
{
	public string Host { get; set; } = null!;

	public short Port { get; set; } = 2323;

	public ushort BufferSize { get; set; } = 4096;
}
