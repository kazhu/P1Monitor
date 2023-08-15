namespace P1Monitor;

public class InfluxDbOptions
{
	public string BaseUrl { get; set; } = "http://127.0.0.1:8086/";

	public string Token { get; set; } = null!;

	public string Organization { get; set; } = "home";

	public string Bucket { get; set; } = "p1";

}
