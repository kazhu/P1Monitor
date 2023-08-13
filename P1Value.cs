using System.Text.RegularExpressions;

namespace P1Monitor;

public abstract record class P1Value(string Id, string FieldName)
{
	public abstract bool IsValid { get; }
}

public partial record class P1TimeValue(string Id, string TextValue, string FieldName) : P1Value(Id, FieldName)
{
	public DateTimeOffset Value => new DateTimeOffset(
		2000 + int.Parse(TextValue.Substring(0, 2)), 
		int.Parse(TextValue.Substring(2, 2)), 
		int.Parse(TextValue.Substring(4, 2)), 
		int.Parse(TextValue.Substring(6, 2)), 
		int.Parse(TextValue.Substring(8, 2)), 
		int.Parse(TextValue.Substring(10, 2)), 
		DateTimeOffset.Now.Offset);
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	[GeneratedRegex(@"^\d{12}S\z")]
	private static partial Regex ValidatorRegex();
}

public record class P1StringValue(string Id, string Value, string FieldName) : P1Value(Id, FieldName)
{
	override public bool IsValid => true;
}

public partial record class P1NoValue(string Id, string TextValue) : P1Value(Id, Id)
{
	public override bool IsValid => true;
}

public partial record class P1OnOffValue(string Id, string TextValue, string FieldName) : P1StringValue(Id, TextValue, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public bool IsOn => TextValue == "ON";

	[GeneratedRegex(@"^(?:ON|OFF)\z")]
	private static partial Regex ValidatorRegex();
}

public abstract record class P1NumberValue(string Id, string FieldName) : P1Value(Id, FieldName)
{
	public abstract decimal Value { get; }
}

public partial record class P1Number4Value(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue);

	[GeneratedRegex(@"^\d+\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1kWhValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\.\d+\*kWh\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1kWValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\.\d+\*kW\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1kvarhValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\.\d+\*kvarh\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1kvarValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\.\d+\*kvar\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1VoltValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\.\d+\*V\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1AmpereValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\*A\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1PowerFactorValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue);

	[GeneratedRegex(@"^\d+\.\d+\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1HzValue(string Id, string TextValue, string FieldName) : P1NumberValue(Id, FieldName)
{
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public override decimal Value => decimal.Parse(TextValue.Substring(0, TextValue.IndexOf('*')));

	[GeneratedRegex(@"^\d+\.\d+\*Hz\z")]
	private static partial Regex ValidatorRegex();
}
