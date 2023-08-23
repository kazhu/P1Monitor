namespace P1Monitor.Options;

public class InfluxDbOptions
{
    public string BaseUrl { get; set; } = null!;

    public string Token { get; set; } = null!;

    public string Organization { get; set; } = null!;

    public string Bucket { get; set; } = null!;

}
